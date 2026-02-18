using System.Buffers;
using System.Globalization;
using System.Text;
using TerraformDotnet.Hcl.Writer.Internal;

namespace TerraformDotnet.Hcl.Writer;

/// <summary>
/// A high-performance, forward-only writer for UTF-8 encoded HCL text.
/// Produces output compatible with <c>terraform fmt</c> canonical style.
/// </summary>
/// <remarks>
/// <para>Uses 2-space indentation, LF newlines, and <c>=</c> alignment
/// within each block body — exactly matching <c>terraform fmt</c>.</para>
/// <para>Writes directly to an <see cref="IBufferWriter{T}"/> for optimal performance.</para>
/// </remarks>
public sealed class Utf8HclWriter : IDisposable
{
    private readonly IBufferWriter<byte> _output;
    private readonly HclWriterOptions _options;
    private readonly bool _ownsOutput;

    private int _currentDepth;
    private long _bytesWritten;
    private bool _isDisposed;

    /// <summary>Gets the number of bytes written so far.</summary>
    public long BytesWritten => _bytesWritten;

    /// <summary>Gets the current block nesting depth.</summary>
    public int CurrentDepth => _currentDepth;

    /// <summary>
    /// Initializes a new writer that writes to an <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <param name="output">The buffer writer to write to.</param>
    /// <param name="options">Writer options. Uses defaults if not specified.</param>
    public Utf8HclWriter(IBufferWriter<byte> output, HclWriterOptions? options = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _options = options ?? HclWriterOptions.Default;
        _ownsOutput = false;
        _currentDepth = 0;
        _bytesWritten = 0;
        _isDisposed = false;
    }

    /// <summary>
    /// Initializes a new writer that writes to a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="options">Writer options.</param>
    public Utf8HclWriter(Stream stream, HclWriterOptions options)
        : this(new StreamBufferWriter(stream), options)
    {
        _ownsOutput = true;
    }

    /// <summary>
    /// Initializes a new writer that writes to a <see cref="Stream"/> with default options.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    public Utf8HclWriter(Stream stream)
        : this(new StreamBufferWriter(stream), null)
    {
        _ownsOutput = true;
    }

    // ── Block structure ─────────────────────────────────────────

    /// <summary>
    /// Writes a block header and opening brace: <c>type "label1" "label2" {</c>.
    /// </summary>
    /// <param name="type">The block type identifier.</param>
    /// <param name="labels">Zero or more block labels.</param>
    public void WriteBlockStart(string type, params string[] labels)
    {
        WriteIndent();
        WriteRaw(Encoding.UTF8.GetBytes(type));
        foreach (string label in labels)
        {
            WriteRaw((byte)' ');
            WriteRaw((byte)'"');
            WriteEscapedString(label);
            WriteRaw((byte)'"');
        }

        WriteRaw(" {"u8);
        WriteNewLine();
        _currentDepth++;
    }

    /// <summary>
    /// Writes a closing brace for the current block at the correct indentation level.
    /// </summary>
    public void WriteBlockEnd()
    {
        _currentDepth--;
        WriteIndent();
        WriteRaw((byte)'}');
        WriteNewLine();
    }

    // ── Attributes ──────────────────────────────────────────────

    /// <summary>
    /// Writes an attribute name followed by <c> = </c>. The caller must then
    /// write the value expression.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="namePadding">Extra spaces after the name for <c>=</c> alignment.</param>
    public void WriteAttributeName(string name, int namePadding = 0)
    {
        WriteIndent();
        WriteRaw(Encoding.UTF8.GetBytes(name));
        for (int i = 0; i < namePadding; i++)
        {
            WriteRaw((byte)' ');
        }

        WriteRaw(" = "u8);
    }

    /// <summary>
    /// Writes a newline to terminate an attribute value.
    /// </summary>
    public void WriteAttributeEnd()
    {
        WriteNewLine();
    }

    // ── Scalar values ───────────────────────────────────────────

    /// <summary>Writes a quoted string value with proper escaping.</summary>
    /// <param name="value">The string to write.</param>
    public void WriteStringValue(string value)
    {
        WriteRaw((byte)'"');
        WriteEscapedString(value);
        WriteRaw((byte)'"');
    }

    /// <summary>Writes an integer value.</summary>
    public void WriteNumberValue(int value)
    {
        WriteRaw(Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Writes a long integer value.</summary>
    public void WriteNumberValue(long value)
    {
        WriteRaw(Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Writes a double value.</summary>
    public void WriteNumberValue(double value)
    {
        WriteRaw(Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Writes a decimal value.</summary>
    public void WriteNumberValue(decimal value)
    {
        WriteRaw(Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Writes a boolean value (<c>true</c> or <c>false</c>).</summary>
    public void WriteBooleanValue(bool value)
    {
        WriteRaw(value ? "true"u8 : "false"u8);
    }

    /// <summary>Writes the <c>null</c> literal.</summary>
    public void WriteNullValue()
    {
        WriteRaw("null"u8);
    }

    // ── Collections ─────────────────────────────────────────────

    /// <summary>Writes the opening bracket of a tuple: <c>[</c>.</summary>
    public void WriteTupleStart()
    {
        WriteRaw((byte)'[');
    }

    /// <summary>Writes the closing bracket of a tuple: <c>]</c>.</summary>
    public void WriteTupleEnd()
    {
        WriteRaw((byte)']');
    }

    /// <summary>Writes the opening brace of an object: <c>{</c>.</summary>
    public void WriteObjectStart()
    {
        WriteRaw((byte)'{');
    }

    /// <summary>Writes the closing brace of an object: <c>}</c>.</summary>
    public void WriteObjectEnd()
    {
        WriteRaw((byte)'}');
    }

    /// <summary>Writes a comma separator: <c>, </c>.</summary>
    public void WriteComma()
    {
        WriteRaw(", "u8);
    }

    /// <summary>Writes an equals sign with spaces: <c> = </c>.</summary>
    public void WriteEquals()
    {
        WriteRaw(" = "u8);
    }

    /// <summary>Writes a colon with spaces: <c> : </c>.</summary>
    public void WriteColon()
    {
        WriteRaw(" : "u8);
    }

    /// <summary>Writes an arrow: <c> =&gt; </c>.</summary>
    public void WriteArrow()
    {
        WriteRaw(" => "u8);
    }

    // ── Comments ────────────────────────────────────────────────

    /// <summary>
    /// Writes a line comment: <c>// text</c>.
    /// </summary>
    /// <param name="text">The comment text (without the <c>//</c> prefix).</param>
    public void WriteLineComment(string text)
    {
        WriteRaw("//"u8);
        WriteRaw(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Writes a hash comment: <c># text</c>.
    /// </summary>
    /// <param name="text">The comment text (without the <c>#</c> prefix).</param>
    public void WriteHashComment(string text)
    {
        WriteRaw((byte)'#');
        WriteRaw(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Writes a block comment: <c>/* text */</c>.
    /// </summary>
    /// <param name="text">The comment text (without the <c>/* */</c> delimiters).</param>
    public void WriteBlockComment(string text)
    {
        WriteRaw("/*"u8);
        WriteRaw(Encoding.UTF8.GetBytes(text));
        WriteRaw("*/"u8);
    }

    // ── Raw output ──────────────────────────────────────────────

    /// <summary>Writes raw HCL bytes to the output.</summary>
    /// <param name="hcl">The raw bytes to write.</param>
    public void WriteRawHcl(ReadOnlySpan<byte> hcl)
    {
        WriteRaw(hcl);
    }

    /// <summary>Writes a raw UTF-8 string to the output.</summary>
    /// <param name="text">The text to write.</param>
    public void WriteRawString(string text)
    {
        WriteRaw(Encoding.UTF8.GetBytes(text));
    }

    // ── Indentation & formatting ────────────────────────────────

    /// <summary>Writes indentation at the current depth (2 spaces per level).</summary>
    public void WriteIndent()
    {
        int spaces = _currentDepth * 2;
        for (int i = 0; i < spaces; i++)
        {
            WriteRaw((byte)' ');
        }
    }

    /// <summary>Writes a newline character.</summary>
    public void WriteNewLine()
    {
        if (_options.NewLineStyle == NewLineStyle.CrLf)
        {
            WriteRaw("\r\n"u8);
        }
        else
        {
            WriteRaw((byte)'\n');
        }
    }

    /// <summary>Writes a single space.</summary>
    public void WriteSpace()
    {
        WriteRaw((byte)' ');
    }

    /// <summary>
    /// Computes the padding needed for <c>=</c> alignment within a body.
    /// </summary>
    /// <param name="attributeNames">The attribute names in the body.</param>
    /// <returns>
    /// A dictionary mapping each name to its padding (spaces to add after the name).
    /// </returns>
    public static Dictionary<string, int> AlignAttributes(IReadOnlyList<string> attributeNames)
    {
        int maxLength = 0;
        foreach (string name in attributeNames)
        {
            if (name.Length > maxLength)
            {
                maxLength = name.Length;
            }
        }

        var result = new Dictionary<string, int>(attributeNames.Count);
        foreach (string name in attributeNames)
        {
            result[name] = maxLength - name.Length;
        }

        return result;
    }

    // ── Lifecycle ───────────────────────────────────────────────

    /// <summary>Flushes all buffered data to the output.</summary>
    public void Flush()
    {
        if (_ownsOutput && _output is StreamBufferWriter sbw)
        {
            sbw.Flush();
        }
    }

    /// <summary>Increments the indentation depth by one level.</summary>
    internal void CurrentDepth_Increment() => _currentDepth++;

    /// <summary>Decrements the indentation depth by one level.</summary>
    internal void CurrentDepth_Decrement() => _currentDepth--;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Flush();

        if (_ownsOutput && _output is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _isDisposed = true;
    }

    // ── Internal write helpers ──────────────────────────────────

    private void WriteRaw(byte b)
    {
        Span<byte> span = _output.GetSpan(1);
        span[0] = b;
        _output.Advance(1);
        _bytesWritten++;
    }

    private void WriteRaw(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        Span<byte> span = _output.GetSpan(data.Length);
        data.CopyTo(span);
        _output.Advance(data.Length);
        _bytesWritten += data.Length;
    }

    private void WriteEscapedString(string value)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': WriteRaw("\\\\"u8); break;
                case '"': WriteRaw("\\\""u8); break;
                case '\n': WriteRaw("\\n"u8); break;
                case '\r': WriteRaw("\\r"u8); break;
                case '\t': WriteRaw("\\t"u8); break;
                case '$': // interpolation markers are written as-is
                    WriteRaw(Encoding.UTF8.GetBytes(c.ToString()));
                    break;
                default:
                    if (c < 0x20)
                    {
                        // Control characters as Unicode escapes
                        WriteRaw(Encoding.UTF8.GetBytes($"\\u{(int)c:X4}"));
                    }
                    else
                    {
                        WriteRaw(Encoding.UTF8.GetBytes(c.ToString()));
                    }

                    break;
            }
        }
    }
}
