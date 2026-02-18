using System.Globalization;
using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;

namespace TerraformDotnet.Hcl.Reader;

public ref partial struct Utf8HclReader
{
    // ── Low-level byte helpers ──────────────────────────────────

    private bool IsAtEnd => _position >= _buffer.Length;

    private byte PeekByte()
    {
        if (_position >= _buffer.Length)
        {
            return 0;
        }

        return _buffer[_position];
    }

    private byte PeekByte(int offset)
    {
        int idx = _position + offset;
        if (idx >= _buffer.Length)
        {
            return 0;
        }

        return _buffer[idx];
    }

    private byte AdvanceByte()
    {
        byte b = _buffer[_position];
        _position++;
        if (b == (byte)'\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        return b;
    }

    private void AdvanceBytes(int count)
    {
        for (int i = 0; i < count; i++)
        {
            AdvanceByte();
        }
    }

    private void MarkTokenStart()
    {
        _tokenStart = _position;
        _tokenStartLine = _line;
        _tokenStartColumn = _column;
    }

    // ── Whitespace & newlines ───────────────────────────────────

    private static bool IsWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t';

    private static bool IsNewline(byte b) => b == (byte)'\n' || b == (byte)'\r';

    private bool IsAtNewline() => !IsAtEnd && IsNewline(PeekByte());

    private void SkipWhitespace()
    {
        while (!IsAtEnd && IsWhitespace(PeekByte()))
        {
            AdvanceByte();
        }
    }

    private void SkipWhitespaceAndNewlines()
    {
        while (!IsAtEnd)
        {
            byte b = PeekByte();
            if (IsWhitespace(b) || IsNewline(b))
            {
                AdvanceByte();
                // Handle CRLF as a single newline (line already incremented in AdvanceByte for \n)
                if (b == (byte)'\r' && !IsAtEnd && PeekByte() == (byte)'\n')
                {
                    _position++;
                    // Don't double-count: AdvanceByte already handled line tracking for \n
                }
            }
            else
            {
                break;
            }
        }
    }

    private void ConsumeNewline()
    {
        if (!IsAtEnd && PeekByte() == (byte)'\r')
        {
            AdvanceByte();
        }

        if (!IsAtEnd && PeekByte() == (byte)'\n')
        {
            AdvanceByte();
        }
    }

    // ── Comment scanning ────────────────────────────────────────

    /// <summary>
    /// Scans a line comment starting with <c>//</c> or <c>#</c>.
    /// The current position should be at the <c>/</c> or <c>#</c>.
    /// </summary>
    private ReadOnlySpan<byte> ScanLineComment()
    {
        int start = _position;

        // Skip '//' or '#'
        if (PeekByte() == (byte)'#')
        {
            AdvanceByte();
        }
        else
        {
            AdvanceByte(); // first '/'
            AdvanceByte(); // second '/'
        }

        int contentStart = _position;

        // Read until newline or EOF
        while (!IsAtEnd && !IsNewline(PeekByte()))
        {
            AdvanceByte();
        }

        return _buffer[contentStart.._position];
    }

    /// <summary>
    /// Scans a block comment <c>/* ... */</c>.
    /// The current position should be at the <c>/</c>.
    /// </summary>
    private ReadOnlySpan<byte> ScanBlockComment()
    {
        int startLine = _line;
        int startCol = _column;

        AdvanceByte(); // '/'
        AdvanceByte(); // '*'

        int contentStart = _position;

        while (!IsAtEnd)
        {
            if (PeekByte() == (byte)'*' && PeekByte(1) == (byte)'/')
            {
                int contentEnd = _position;
                AdvanceByte(); // '*'
                AdvanceByte(); // '/'
                return _buffer[contentStart..contentEnd];
            }

            AdvanceByte();
        }

        throw new HclSyntaxException(
            "Unterminated block comment.",
            new Mark(_tokenStart, startLine, startCol));
    }

    /// <summary>
    /// Skips or emits a comment if one is present at the current position.
    /// Returns true if a comment was consumed.
    /// </summary>
    private bool TryConsumeComment()
    {
        if (IsAtEnd)
        {
            return false;
        }

        byte b = PeekByte();

        if (b == (byte)'#')
        {
            MarkTokenStart();
            ReadOnlySpan<byte> content = ScanLineComment();
            _commentMarker = (byte)'#';
            if (_options.ReadComments)
            {
                _tokenType = HclTokenType.Comment;
                _valueSpan = content;
                _hasValue = true;
            }

            return true;
        }

        if (b == (byte)'/' && _position + 1 < _buffer.Length)
        {
            byte next = PeekByte(1);
            if (next == (byte)'/')
            {
                MarkTokenStart();
                ReadOnlySpan<byte> content = ScanLineComment();
                _commentMarker = (byte)'/';
                if (_options.ReadComments)
                {
                    _tokenType = HclTokenType.Comment;
                    _valueSpan = content;
                    _hasValue = true;
                }

                return true;
            }

            if (next == (byte)'*')
            {
                MarkTokenStart();
                ReadOnlySpan<byte> content = ScanBlockComment();
                _commentMarker = (byte)'*';
                if (_options.ReadComments)
                {
                    _tokenType = HclTokenType.Comment;
                    _valueSpan = content;
                    _hasValue = true;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Skips all whitespace, newlines, and comments.
    /// If ReadComments is enabled, stops and emits the first comment found.
    /// Returns true if a comment token was emitted.
    /// </summary>
    private bool SkipTrivia()
    {
        while (!IsAtEnd)
        {
            byte b = PeekByte();

            if (IsWhitespace(b) || IsNewline(b))
            {
                SkipWhitespaceAndNewlines();
                continue;
            }

            if (b == (byte)'#' ||
                (b == (byte)'/' && _position + 1 < _buffer.Length &&
                 (PeekByte(1) == (byte)'/' || PeekByte(1) == (byte)'*')))
            {
                if (_options.ReadComments)
                {
                    // Let TryConsumeComment emit the comment token
                    return false;
                }

                // Skip the comment silently
                TryConsumeComment();
                continue;
            }

            break;
        }

        return false;
    }

    // ── Identifier scanning ─────────────────────────────────────

    private static bool IsIdStart(byte b)
    {
        // ASCII subset of UAX#31 ID_Start plus underscore
        return (b >= (byte)'a' && b <= (byte)'z') ||
               (b >= (byte)'A' && b <= (byte)'Z') ||
               b == (byte)'_';
    }

    private static bool IsIdContinue(byte b)
    {
        // ASCII subset of UAX#31 ID_Continue plus dash (HCL extension)
        return (b >= (byte)'a' && b <= (byte)'z') ||
               (b >= (byte)'A' && b <= (byte)'Z') ||
               (b >= (byte)'0' && b <= (byte)'9') ||
               b == (byte)'_' ||
               b == (byte)'-';
    }

    private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

    /// <summary>
    /// Checks if the byte is the start of a multi-byte UTF-8 sequence.
    /// </summary>
    private static bool IsUtf8MultiByteStart(byte b) => b >= 0xC0;

    /// <summary>
    /// Returns the length of a UTF-8 code point sequence starting at the given byte.
    /// </summary>
    private static int Utf8SequenceLength(byte b)
    {
        if (b < 0x80)
        {
            return 1;
        }

        if (b < 0xC0)
        {
            return 0; // continuation byte, invalid as start
        }

        if (b < 0xE0)
        {
            return 2;
        }

        if (b < 0xF0)
        {
            return 3;
        }

        return 4;
    }

    /// <summary>
    /// Checks if the bytes at the current position form a Unicode ID_Start character.
    /// </summary>
    private bool IsUnicodeIdStart()
    {
        if (_position >= _buffer.Length)
        {
            return false;
        }

        byte b = _buffer[_position];
        if (!IsUtf8MultiByteStart(b))
        {
            return false;
        }

        int len = Utf8SequenceLength(b);
        if (_position + len > _buffer.Length)
        {
            return false;
        }

        var rune = DecodeUtf8Rune(_buffer.Slice(_position, len));
        return Rune.IsLetter(rune);
    }

    /// <summary>
    /// Checks if the bytes at the current position form a Unicode ID_Continue character.
    /// </summary>
    private bool IsUnicodeIdContinue()
    {
        if (_position >= _buffer.Length)
        {
            return false;
        }

        byte b = _buffer[_position];
        if (!IsUtf8MultiByteStart(b))
        {
            return false;
        }

        int len = Utf8SequenceLength(b);
        if (_position + len > _buffer.Length)
        {
            return false;
        }

        var rune = DecodeUtf8Rune(_buffer.Slice(_position, len));
        return Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.ConnectorPunctuation;
    }

    private static Rune DecodeUtf8Rune(ReadOnlySpan<byte> bytes)
    {
        Rune.DecodeFromUtf8(bytes, out Rune rune, out _);
        return rune;
    }

    /// <summary>
    /// Scans an identifier: <c>ID_Start (ID_Continue | '-')*</c>.
    /// </summary>
    private ReadOnlySpan<byte> ScanIdentifier()
    {
        int start = _position;

        // First character: ID_Start (or multi-byte Unicode letter)
        byte b = PeekByte();
        if (IsIdStart(b))
        {
            AdvanceByte();
        }
        else if (IsUtf8MultiByteStart(b))
        {
            int len = Utf8SequenceLength(b);
            AdvanceBytes(len);
        }

        // Continue: ID_Continue | '-' (or multi-byte Unicode)
        while (!IsAtEnd)
        {
            b = PeekByte();
            if (IsIdContinue(b))
            {
                AdvanceByte();
            }
            else if (IsUtf8MultiByteStart(b) && IsUnicodeIdContinue())
            {
                int len = Utf8SequenceLength(b);
                AdvanceBytes(len);
            }
            else
            {
                break;
            }
        }

        return _buffer[start.._position];
    }

    // ── Number scanning ─────────────────────────────────────────

    /// <summary>
    /// Scans a numeric literal: <c>decimal+ ("." decimal+)? (expmark decimal+)?</c>.
    /// </summary>
    private ReadOnlySpan<byte> ScanNumber()
    {
        int start = _position;

        // Integer part
        while (!IsAtEnd && IsDigit(PeekByte()))
        {
            AdvanceByte();
        }

        // Fractional part
        if (!IsAtEnd && PeekByte() == (byte)'.')
        {
            // Check that the next byte after dot is a digit (not '..' or '.identifier')
            if (_position + 1 < _buffer.Length && IsDigit(PeekByte(1)))
            {
                AdvanceByte(); // '.'
                while (!IsAtEnd && IsDigit(PeekByte()))
                {
                    AdvanceByte();
                }
            }
        }

        // Exponent part
        if (!IsAtEnd && (PeekByte() == (byte)'e' || PeekByte() == (byte)'E'))
        {
            AdvanceByte(); // 'e' or 'E'

            if (!IsAtEnd && (PeekByte() == (byte)'+' || PeekByte() == (byte)'-'))
            {
                AdvanceByte();
            }

            if (IsAtEnd || !IsDigit(PeekByte()))
            {
                throw new HclSyntaxException(
                    "Invalid number: exponent has no digits.",
                    new Mark(start, _tokenStartLine, _tokenStartColumn));
            }

            while (!IsAtEnd && IsDigit(PeekByte()))
            {
                AdvanceByte();
            }
        }

        return _buffer[start.._position];
    }

    // ── Operator / delimiter scanning ───────────────────────────

    /// <summary>
    /// Tries to scan an operator or delimiter token.
    /// Returns <see cref="HclTokenType.None"/> if the byte is not an operator.
    /// </summary>
    private HclTokenType ScanOperatorOrDelimiter()
    {
        byte b = PeekByte();
        switch (b)
        {
            case (byte)'{':
                AdvanceByte();
                return HclTokenType.OpenBrace;
            case (byte)'}':
                AdvanceByte();
                return HclTokenType.CloseBrace;
            case (byte)'[':
                AdvanceByte();
                return HclTokenType.OpenBracket;
            case (byte)']':
                AdvanceByte();
                return HclTokenType.CloseBracket;
            case (byte)'(':
                AdvanceByte();
                return HclTokenType.OpenParen;
            case (byte)')':
                AdvanceByte();
                return HclTokenType.CloseParen;
            case (byte)'+':
                AdvanceByte();
                return HclTokenType.Plus;
            case (byte)'-':
                AdvanceByte();
                return HclTokenType.Minus;
            case (byte)'*':
                AdvanceByte();
                return HclTokenType.Star;
            case (byte)'/':
                AdvanceByte();
                return HclTokenType.Slash;
            case (byte)'%':
                if (PeekByte(1) == (byte)'{')
                {
                    AdvanceBytes(2);
                    return HclTokenType.PercentBrace;
                }

                AdvanceByte();
                return HclTokenType.Percent;
            case (byte)'?':
                AdvanceByte();
                return HclTokenType.Question;
            case (byte)':':
                AdvanceByte();
                return HclTokenType.Colon;
            case (byte)',':
                AdvanceByte();
                return HclTokenType.Comma;
            case (byte)'.':
                if (PeekByte(1) == (byte)'.' && PeekByte(2) == (byte)'.')
                {
                    AdvanceBytes(3);
                    return HclTokenType.Ellipsis;
                }

                AdvanceByte();
                return HclTokenType.Dot;
            case (byte)'=':
                if (PeekByte(1) == (byte)'=')
                {
                    AdvanceBytes(2);
                    return HclTokenType.EqualEqual;
                }

                if (PeekByte(1) == (byte)'>')
                {
                    AdvanceBytes(2);
                    return HclTokenType.FatArrow;
                }

                AdvanceByte();
                return HclTokenType.Equals;
            case (byte)'!':
                if (PeekByte(1) == (byte)'=')
                {
                    AdvanceBytes(2);
                    return HclTokenType.NotEqual;
                }

                AdvanceByte();
                return HclTokenType.Not;
            case (byte)'<':
                if (PeekByte(1) == (byte)'=')
                {
                    AdvanceBytes(2);
                    return HclTokenType.LessEqual;
                }

                if (PeekByte(1) == (byte)'<')
                {
                    // Heredoc — don't consume, handled elsewhere
                    return HclTokenType.None;
                }

                AdvanceByte();
                return HclTokenType.LessThan;
            case (byte)'>':
                if (PeekByte(1) == (byte)'=')
                {
                    AdvanceBytes(2);
                    return HclTokenType.GreaterEqual;
                }

                AdvanceByte();
                return HclTokenType.GreaterThan;
            case (byte)'&':
                if (PeekByte(1) == (byte)'&')
                {
                    AdvanceBytes(2);
                    return HclTokenType.And;
                }

                break;
            case (byte)'|':
                if (PeekByte(1) == (byte)'|')
                {
                    AdvanceBytes(2);
                    return HclTokenType.Or;
                }

                break;
            case (byte)'$':
                if (PeekByte(1) == (byte)'{')
                {
                    AdvanceBytes(2);
                    return HclTokenType.DollarBrace;
                }

                break;
        }

        return HclTokenType.None;
    }
}
