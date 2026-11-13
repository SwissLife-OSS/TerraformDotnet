using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Writer;

/// <summary>
/// Applies the layout rules of <c>terraform fmt</c> to HCL text: indentation, <c>=</c> alignment and
/// trailing comment alignment.
/// </summary>
/// <remarks>
/// <para>Only the whitespace at the start of a line, before the first <c>=</c>, before a trailing
/// comment and at the end of a line changes. Line breaks, the spacing inside a line, heredoc bodies
/// and the lines inside a multi-line block comment are kept as written.</para>
/// <para>The rules:</para>
/// <list type="number">
/// <item>Indentation: a line that opens more brackets than it closes is printed at the current level
/// and adds one level, which remembers how many brackets it opened. A line that closes more than it
/// opens first gives back that many brackets, dropping a level each time its count is used up, and
/// is then printed at the resulting level. Other lines are printed at the current level.</item>
/// <item>Adjacent lines whose text from the first <c>=</c> to the end of the line has balanced
/// brackets have their <c>=</c> aligned one space after the longest text before it. Any other
/// first <c>=</c> gets one space before it.</item>
/// <item>Adjacent lines that end with a <c>#</c> or <c>//</c> comment, after other content, have
/// their comments aligned one space after the longest content.</item>
/// </list>
/// <para>Bracket counting stops at a heredoc, whose body is kept as written.</para>
/// </remarks>
public static class HclFormatter
{
    /// <summary>
    /// Formats HCL text.
    /// </summary>
    /// <param name="hcl">The HCL text.</param>
    /// <returns>The formatted text.</returns>
    /// <exception cref="Exceptions.HclSyntaxException">The text is not valid HCL.</exception>
    public static string Format(string hcl)
    {
        ArgumentNullException.ThrowIfNull(hcl);

        byte[] utf8 = Encoding.UTF8.GetBytes(hcl);
        HclFileParser.Parse(utf8, preserveComments: false);

        var output = new ArrayBufferWriter<byte>(hcl.Length + 64);
        Format(utf8, 0, output);

        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    /// <summary>
    /// Formats UTF-8 encoded HCL text.
    /// </summary>
    /// <param name="utf8Hcl">The UTF-8 encoded HCL text.</param>
    /// <returns>The formatted text, UTF-8 encoded.</returns>
    /// <exception cref="Exceptions.HclSyntaxException">The text is not valid HCL.</exception>
    public static byte[] Format(ReadOnlySpan<byte> utf8Hcl)
    {
        HclFileParser.Parse(utf8Hcl, preserveComments: false);

        var output = new ArrayBufferWriter<byte>(utf8Hcl.Length + 64);
        Format(utf8Hcl, 0, output);

        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Formats UTF-8 encoded HCL text and writes it to <paramref name="output"/>, with every
    /// line indented by <paramref name="baseLevel"/> extra levels.
    /// </summary>
    internal static void Format(ReadOnlySpan<byte> hcl, int baseLevel, IBufferWriter<byte> output)
    {
        LineTable lines = LineTable.Create(hcl);
        List<FormatToken> tokens = Tokenize(hcl);
        List<FormatLine> formatLines = BuildLines(hcl, lines, tokens);

        ComputeIndents(formatLines, tokens, baseLevel);
        MeasureCells(hcl, formatLines);
        AlignAssignments(formatLines);
        AlignComments(formatLines);
        Write(hcl, lines, formatLines, output);
    }

    // ── Tokens ──────────────────────────────────────────────────

    private enum TokenKind : byte
    {
        Other,
        Open,
        Close,
        Equals,
        LineComment,
        Heredoc,
    }

    private readonly record struct FormatToken(TokenKind Kind, int Start, int End);

    private static List<FormatToken> Tokenize(ReadOnlySpan<byte> hcl)
    {
        var tokens = new List<FormatToken>();
        int depth = 0;
        var reader = new Utf8HclReader(hcl, new HclReaderOptions { ReadComments = true, MaxDepth = int.MaxValue });

        while (reader.Read())
        {
            int start = reader.TokenStart.Offset;
            int end = reader.Position.Offset;

            switch (reader.TokenType)
            {
                case HclTokenType.Eof:
                    if (depth > 0)
                    {
                        throw new HclSyntaxException("Unexpected end of input: a bracket or block is not closed.", reader.Position);
                    }

                    return tokens;

                case HclTokenType.AttributeName:
                    // The reader consumes the '=' together with the name.
                    tokens.Add(new FormatToken(TokenKind.Other, start, end - 1));
                    tokens.Add(new FormatToken(TokenKind.Equals, end - 1, end));
                    break;

                case HclTokenType.OpenBrace:
                case HclTokenType.OpenBracket:
                case HclTokenType.OpenParen:
                case HclTokenType.BlockStart:
                    tokens.Add(new FormatToken(TokenKind.Open, start, start + 1));
                    depth++;
                    break;

                case HclTokenType.CloseBrace:
                case HclTokenType.CloseBracket:
                case HclTokenType.CloseParen:
                case HclTokenType.BlockEnd:
                    tokens.Add(new FormatToken(TokenKind.Close, start, start + 1));
                    depth--;
                    break;

                case HclTokenType.Equals:
                    tokens.Add(new FormatToken(TokenKind.Equals, start, start + 1));
                    break;

                case HclTokenType.Comment:
                    tokens.Add(new FormatToken(
                        reader.CommentMarker == (byte)'*' ? TokenKind.Other : TokenKind.LineComment,
                        start,
                        end));
                    break;

                case HclTokenType.StringLiteral when reader.StringStyle == StringLiteralStyle.Heredoc:
                    tokens.Add(new FormatToken(TokenKind.Heredoc, start, end));
                    break;

                default:
                    tokens.Add(new FormatToken(TokenKind.Other, start, Math.Max(start + 1, end)));
                    break;
            }
        }

        throw new HclSyntaxException("Unexpected end of input.", reader.Position);
    }

    // ── Lines ───────────────────────────────────────────────────

    /// <summary>
    /// A line as <c>terraform fmt</c> sees it: one physical line, plus the following lines when a
    /// token (a heredoc or a block comment) continues onto them.
    /// </summary>
    private sealed class FormatLine
    {
        public int FirstLine;
        public int LastLine;
        public int FirstToken;
        public int TokenCount;

        public int Indent;
        public int ContentStart;
        public int ContentEnd;

        /// <summary>Offset of the first assignment <c>=</c>, or -1.</summary>
        public int AssignStart = -1;

        /// <summary>Whether the <c>=</c> takes part in alignment: its value ends on this line.</summary>
        public bool AlignsAssignment;

        /// <summary>Offset of the trailing comment, or -1.</summary>
        public int CommentStart = -1;

        public int LeadEnd;
        public int AssignEnd;
        public int LeadColumns;
        public int AssignColumns;
        public int AssignPadding = 1;
        public int CommentPadding = 1;

        public bool IsBlank => TokenCount == 0;
    }

    private static List<FormatLine> BuildLines(ReadOnlySpan<byte> hcl, LineTable lines, List<FormatToken> tokens)
    {
        var result = new List<FormatLine>(lines.Count);
        int tokenIndex = 0;
        int line = 0;

        while (line < lines.Count)
        {
            var formatLine = new FormatLine { FirstLine = line, LastLine = line, FirstToken = tokenIndex };

            while (tokenIndex < tokens.Count && lines.LineOf(tokens[tokenIndex].Start) <= formatLine.LastLine)
            {
                FormatToken token = tokens[tokenIndex];
                int tokenLastLine = lines.LineOf(Math.Max(token.Start, token.End - 1));
                formatLine.LastLine = Math.Max(formatLine.LastLine, tokenLastLine);
                tokenIndex++;
            }

            formatLine.TokenCount = tokenIndex - formatLine.FirstToken;

            if (!formatLine.IsBlank)
            {
                formatLine.ContentStart = tokens[formatLine.FirstToken].Start;
                formatLine.ContentEnd = TrimEnd(hcl, formatLine.ContentStart, lines.ContentEnd(formatLine.LastLine));
                SplitCells(hcl, formatLine, tokens);
            }

            result.Add(formatLine);
            line = formatLine.LastLine + 1;
        }

        return result;
    }

    private static void SplitCells(ReadOnlySpan<byte> hcl, FormatLine line, List<FormatToken> tokens)
    {
        int first = line.FirstToken;
        int count = line.TokenCount;

        if (count > 1 && tokens[first + count - 1].Kind == TokenKind.LineComment)
        {
            line.CommentStart = tokens[first + count - 1].Start;
            count--;
        }

        int leadEnd = line.CommentStart >= 0
            ? TrimEnd(hcl, line.ContentStart, line.CommentStart)
            : line.ContentEnd;

        line.LeadEnd = leadEnd;

        for (int i = 1; i < count; i++)
        {
            if (tokens[first + i].Kind != TokenKind.Equals)
            {
                continue;
            }

            line.AssignStart = tokens[first + i].Start;
            line.LeadEnd = TrimEnd(hcl, line.ContentStart, line.AssignStart);
            line.AssignEnd = leadEnd;

            // A value that opens a multi-line expression breaks the alignment chain.
            line.AlignsAssignment = NetBrackets(tokens, first + i, first + count) == 0;

            break;
        }
    }

    private static int NetBrackets(List<FormatToken> tokens, int from, int to)
    {
        int net = 0;

        for (int i = from; i < to; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.Open:
                    net++;
                    break;
                case TokenKind.Close:
                    net--;
                    break;
                case TokenKind.Heredoc:
                    return net;
            }
        }

        return net;
    }

    // ── Rules ───────────────────────────────────────────────────

    private static void ComputeIndents(List<FormatLine> lines, List<FormatToken> tokens, int baseLevel)
    {
        var levels = new List<int>();

        foreach (FormatLine line in lines)
        {
            if (line.IsBlank)
            {
                continue;
            }

            int net = NetBrackets(tokens, line.FirstToken, line.FirstToken + line.TokenCount);

            if (net > 0)
            {
                line.Indent = 2 * (baseLevel + levels.Count);
                levels.Add(net);

                continue;
            }

            int closed = -net;
            while (closed > 0 && levels.Count > 0)
            {
                int top = levels[^1];

                if (closed >= top)
                {
                    closed -= top;
                    levels.RemoveAt(levels.Count - 1);
                }
                else
                {
                    levels[^1] = top - closed;
                    closed = 0;
                }
            }

            line.Indent = 2 * (baseLevel + levels.Count);
        }
    }

    private static void MeasureCells(ReadOnlySpan<byte> hcl, List<FormatLine> lines)
    {
        foreach (FormatLine line in lines)
        {
            if (line.IsBlank)
            {
                continue;
            }

            line.LeadColumns = line.Indent + Columns(hcl[line.ContentStart..line.LeadEnd]);

            if (line.AssignStart >= 0)
            {
                line.AssignColumns = Columns(hcl[line.AssignStart..line.AssignEnd]);
            }
        }
    }

    private static void AlignAssignments(List<FormatLine> lines) =>
        Align(
            lines,
            static line => line.AlignsAssignment,
            static line => line.LeadColumns,
            static (line, padding) => line.AssignPadding = padding);

    private static void AlignComments(List<FormatLine> lines) =>
        Align(
            lines,
            static line => line.CommentStart >= 0,
            static line => line.AssignStart >= 0
                ? line.LeadColumns + line.AssignPadding + line.AssignColumns
                : line.LeadColumns,
            static (line, padding) => line.CommentPadding = padding);

    /// <summary>
    /// Pads a cell on each run of adjacent lines that have it so that the cells start one column
    /// after the widest text before them.
    /// </summary>
    private static void Align(
        List<FormatLine> lines,
        Func<FormatLine, bool> hasCell,
        Func<FormatLine, int> columnsBefore,
        Action<FormatLine, int> setPadding)
    {
        int chainStart = 0;

        for (int i = 0; i <= lines.Count; i++)
        {
            if (i < lines.Count && hasCell(lines[i]))
            {
                continue;
            }

            int maxColumns = 0;

            for (int j = chainStart; j < i; j++)
            {
                maxColumns = Math.Max(maxColumns, columnsBefore(lines[j]));
            }

            for (int j = chainStart; j < i; j++)
            {
                setPadding(lines[j], maxColumns - columnsBefore(lines[j]) + 1);
            }

            chainStart = i + 1;
        }
    }

    /// <summary>Counts characters the way <c>terraform fmt</c> does: one column per code point.</summary>
    private static int Columns(ReadOnlySpan<byte> utf8)
    {
        int columns = 0;

        foreach (byte b in utf8)
        {
            if ((b & 0xC0) != 0x80)
            {
                columns++;
            }
        }

        return columns;
    }

    // ── Output ──────────────────────────────────────────────────

    private static void Write(ReadOnlySpan<byte> hcl, LineTable lines, List<FormatLine> formatLines, IBufferWriter<byte> output)
    {
        foreach (FormatLine line in formatLines)
        {
            if (!line.IsBlank)
            {
                WriteSpaces(output, line.Indent);
                output.Write(hcl[line.ContentStart..line.LeadEnd]);

                if (line.AssignStart >= 0)
                {
                    WriteSpaces(output, line.AssignPadding);
                    output.Write(hcl[line.AssignStart..line.AssignEnd]);
                }

                if (line.CommentStart >= 0)
                {
                    WriteSpaces(output, line.CommentPadding);
                    output.Write(hcl[line.CommentStart..line.ContentEnd]);
                }
            }

            output.Write(lines.Terminator(hcl, line.LastLine));
        }
    }

    private static void WriteSpaces(IBufferWriter<byte> output, int count)
    {
        if (count <= 0)
        {
            return;
        }

        Span<byte> span = output.GetSpan(count);
        span[..count].Fill((byte)' ');
        output.Advance(count);
    }

    private static int TrimEnd(ReadOnlySpan<byte> hcl, int start, int end)
    {
        while (end > start && hcl[end - 1] is (byte)' ' or (byte)'\t')
        {
            end--;
        }

        return end;
    }

    /// <summary>Start and content end offsets of the physical lines.</summary>
    private readonly struct LineTable
    {
        private readonly int[] _starts;
        private readonly int[] _contentEnds;
        private readonly int _length;

        private LineTable(int[] starts, int[] contentEnds, int length)
        {
            _starts = starts;
            _contentEnds = contentEnds;
            _length = length;
        }

        public int Count => _starts.Length;

        public static LineTable Create(ReadOnlySpan<byte> hcl)
        {
            var starts = new List<int> { 0 };
            var contentEnds = new List<int>();

            for (int i = 0; i < hcl.Length; i++)
            {
                if (hcl[i] != (byte)'\n')
                {
                    continue;
                }

                contentEnds.Add(i > 0 && hcl[i - 1] == (byte)'\r' ? i - 1 : i);

                if (i + 1 < hcl.Length)
                {
                    starts.Add(i + 1);
                }
            }

            if (contentEnds.Count < starts.Count)
            {
                contentEnds.Add(hcl.Length);
            }

            return new LineTable([.. starts], [.. contentEnds], hcl.Length);
        }

        /// <summary>Gets the line that contains the offset; a line break belongs to the line it ends.</summary>
        public int LineOf(int offset)
        {
            int index = Array.BinarySearch(_starts, offset);

            return index >= 0 ? index : ~index - 1;
        }

        /// <summary>Gets the end of the line's text, without its line break.</summary>
        public int ContentEnd(int line) => _contentEnds[line];

        public ReadOnlySpan<byte> Terminator(ReadOnlySpan<byte> hcl, int line)
        {
            int end = line + 1 < _starts.Length ? _starts[line + 1] : _length;

            return hcl[_contentEnds[line]..end];
        }
    }
}
