using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;

namespace TerraformDotnet.Hcl.Reader;

public ref partial struct Utf8HclReader
{
    // ── Quoted string scanning ──────────────────────────────────

    /// <summary>
    /// Scans a quoted string literal. Current position must be at the opening <c>"</c>.
    /// Returns the raw bytes between the quotes (including escape sequences).
    /// Template sequences (<c>${</c>, <c>%{</c>) are NOT expanded here — they are
    /// part of the raw value and are handled at the expression level.
    /// </summary>
    private ReadOnlySpan<byte> ScanQuotedString()
    {
        int startLine = _line;
        int startCol = _column;
        AdvanceByte(); // opening '"'

        int contentStart = _position;
        int templateDepth = 0;

        while (!IsAtEnd)
        {
            byte b = PeekByte();

            if (b == (byte)'"' && templateDepth == 0)
            {
                int contentEnd = _position;
                AdvanceByte(); // closing '"'
                _stringStyle = StringLiteralStyle.Quoted;
                return _buffer[contentStart..contentEnd];
            }

            if (b == (byte)'\\')
            {
                // Escape sequence — skip the backslash and the next character
                AdvanceByte(); // '\'
                if (IsAtEnd)
                {
                    throw new HclSyntaxException(
                        "Unterminated string: unexpected end after escape character.",
                        new Mark(_tokenStart, startLine, startCol));
                }

                AdvanceByte(); // escaped char
                // For \uNNNN, \UNNNNNNNN the remaining hex digits are consumed as regular chars
                continue;
            }

            if (b == (byte)'\n' || b == (byte)'\r')
            {
                throw new HclSyntaxException(
                    "Unterminated string: literal newline in quoted string.",
                    new Mark(_tokenStart, startLine, startCol));
            }

            // Track template nesting: ${ and %{ open, } closes
            if (templateDepth > 0 && b == (byte)'}')
            {
                templateDepth--;
                AdvanceByte();
                continue;
            }

            if ((b == (byte)'$' || b == (byte)'%') && PeekByte(1) == (byte)'{')
            {
                // Check for escaped: $$ or %%
                if (_position >= 1 && _position - 1 >= contentStart)
                {
                    // Escaped sequences $${ and %%{ are literal — don't track depth
                    byte prev = _buffer[_position - 1];
                    if ((b == (byte)'$' && prev == (byte)'$') || (b == (byte)'%' && prev == (byte)'%'))
                    {
                        AdvanceByte();
                        continue;
                    }
                }

                templateDepth++;
                AdvanceBytes(2); // '${' or '%{'
                continue;
            }

            AdvanceByte();
        }

        throw new HclSyntaxException(
            "Unterminated string literal.",
            new Mark(_tokenStart, startLine, startCol));
    }

    // ── Heredoc scanning ────────────────────────────────────────

    /// <summary>
    /// Scans a heredoc string. Current position must be at the first <c>&lt;</c>.
    /// Returns the raw content bytes between the opening marker line and the closing marker.
    /// </summary>
    private ReadOnlySpan<byte> ScanHeredoc()
    {
        int startLine = _line;
        int startCol = _column;

        AdvanceByte(); // first '<'
        AdvanceByte(); // second '<'

        bool indented = false;
        if (!IsAtEnd && PeekByte() == (byte)'-')
        {
            indented = true;
            AdvanceByte(); // '-'
        }

        _stringStyle = indented ? StringLiteralStyle.IndentedHeredoc : StringLiteralStyle.Heredoc;

        // Read the marker identifier
        int markerStart = _position;
        while (!IsAtEnd && !IsNewline(PeekByte()) && !IsWhitespace(PeekByte()))
        {
            AdvanceByte();
        }

        if (_position == markerStart)
        {
            throw new HclSyntaxException(
                "Heredoc requires a marker identifier after '<<'.",
                new Mark(_tokenStart, startLine, startCol));
        }

        ReadOnlySpan<byte> marker = _buffer[markerStart.._position];

        // Consume the newline after the marker declaration
        if (IsAtEnd)
        {
            throw new HclSyntaxException(
                "Unterminated heredoc: unexpected end after marker.",
                new Mark(_tokenStart, startLine, startCol));
        }

        ConsumeNewline();

        // Read content until we find the marker on its own line
        int contentStart = _position;

        while (!IsAtEnd)
        {
            int lineStart = _position;

            // Check if this line starts with (optional whitespace +) marker
            int scanPos = _position;

            // Skip leading whitespace (only meaningful for indented heredocs)
            while (scanPos < _buffer.Length && IsWhitespace(_buffer[scanPos]))
            {
                scanPos++;
            }

            // Check if the rest of the line matches the marker
            if (scanPos + marker.Length <= _buffer.Length)
            {
                ReadOnlySpan<byte> candidate = _buffer.Slice(scanPos, marker.Length);
                if (candidate.SequenceEqual(marker))
                {
                    // Verify the marker is alone on its line (only newline or EOF after)
                    int afterMarker = scanPos + marker.Length;
                    if (afterMarker >= _buffer.Length ||
                        IsNewline(_buffer[afterMarker]))
                    {
                        int contentEnd = lineStart;
                        ReadOnlySpan<byte> content = _buffer[contentStart..contentEnd];

                        // Advance past the marker line
                        _position = afterMarker;
                        _column += afterMarker - lineStart;
                        if (!IsAtEnd)
                        {
                            ConsumeNewline();
                        }

                        if (indented)
                        {
                            return content; // Indentation stripping happens at decode time
                        }

                        return content;
                    }
                }
            }

            // Not the closing marker — advance to next line
            while (!IsAtEnd && !IsNewline(PeekByte()))
            {
                AdvanceByte();
            }

            if (!IsAtEnd)
            {
                ConsumeNewline();
            }
        }

        throw new HclSyntaxException(
            $"Unterminated heredoc: closing marker not found.",
            new Mark(_tokenStart, startLine, startCol));
    }

    // ── String decode helpers ───────────────────────────────────

    /// <summary>
    /// Decodes a raw string span (from a quoted string) by processing escape sequences.
    /// </summary>
    private static string DecodeStringValue(ReadOnlySpan<byte> raw)
    {
        // Fast path: no backslash, no escaping needed
        if (raw.IndexOf((byte)'\\') < 0)
        {
            return Encoding.UTF8.GetString(raw);
        }

        var sb = new StringBuilder(raw.Length);
        int i = 0;

        while (i < raw.Length)
        {
            byte b = raw[i];

            if (b != (byte)'\\')
            {
                // Decode UTF-8 character
                int len = Utf8SequenceLength(b);
                if (len <= 0)
                {
                    len = 1;
                }

                if (i + len <= raw.Length)
                {
                    string ch = Encoding.UTF8.GetString(raw.Slice(i, len));
                    sb.Append(ch);
                    i += len;
                }
                else
                {
                    sb.Append((char)b);
                    i++;
                }

                continue;
            }

            // Escape sequence
            i++; // skip backslash
            if (i >= raw.Length)
            {
                break;
            }

            byte esc = raw[i];
            i++;

            switch (esc)
            {
                case (byte)'n':
                    sb.Append('\n');
                    break;
                case (byte)'r':
                    sb.Append('\r');
                    break;
                case (byte)'t':
                    sb.Append('\t');
                    break;
                case (byte)'"':
                    sb.Append('"');
                    break;
                case (byte)'\\':
                    sb.Append('\\');
                    break;
                case (byte)'u':
                    // \uNNNN — 4 hex digits
                    if (i + 4 <= raw.Length)
                    {
                        string hex = Encoding.UTF8.GetString(raw.Slice(i, 4));
                        int codePoint = Convert.ToInt32(hex, 16);
                        sb.Append((char)codePoint);
                        i += 4;
                    }

                    break;
                case (byte)'U':
                    // \UNNNNNNNN — 8 hex digits
                    if (i + 8 <= raw.Length)
                    {
                        string hex = Encoding.UTF8.GetString(raw.Slice(i, 8));
                        int codePoint = Convert.ToInt32(hex, 16);
                        sb.Append(char.ConvertFromUtf32(codePoint));
                        i += 8;
                    }

                    break;
                default:
                    sb.Append('\\');
                    sb.Append((char)esc);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips the minimum leading whitespace from an indented heredoc.
    /// </summary>
    internal static string DecodeIndentedHeredoc(ReadOnlySpan<byte> raw)
    {
        string text = Encoding.UTF8.GetString(raw);
        if (text.Length == 0)
        {
            return text;
        }

        string[] lines = text.Split('\n');
        int minIndent = int.MaxValue;

        // Find minimum indentation (ignoring empty lines)
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            int indent = 0;
            while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
            {
                indent++;
            }

            if (indent < minIndent)
            {
                minIndent = indent;
            }
        }

        if (minIndent == int.MaxValue)
        {
            minIndent = 0;
        }

        // Strip the common indentation
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length >= minIndent)
            {
                sb.Append(line.AsSpan(minIndent));
            }

            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }
}
