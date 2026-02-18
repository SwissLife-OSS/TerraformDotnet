using System.Globalization;
using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;

namespace TerraformDotnet.Hcl.Reader;

/// <summary>
/// High-performance, zero-copy HCLv2 reader over UTF-8 encoded bytes.
/// Operates as a pull-based tokenizer: call <see cref="Read"/> repeatedly
/// to advance through the token stream.
/// </summary>
/// <remarks>
/// This is a <c>ref struct</c> to stay on the stack and work with
/// <see cref="ReadOnlySpan{T}"/>. It cannot be boxed or stored on the heap.
/// </remarks>
public ref partial struct Utf8HclReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private readonly HclReaderOptions _options;
    private readonly bool _isFinalBlock;

    private int _position;
    private int _line;
    private int _column;
    private int _tokenStart;
    private int _tokenStartLine;
    private int _tokenStartColumn;
    private int _currentDepth;
    private HclTokenType _tokenType;
    private ReadOnlySpan<byte> _valueSpan;
    private StringLiteralStyle _stringStyle;
    private byte _commentMarker;

#pragma warning disable CS0414 // Assigned but value never read — reserved for streaming
    private bool _hasValue;
#pragma warning restore CS0414

    // Context tracking for structural parsing
    private bool _inBody;
    private bool _expectingValue;
    private int _expressionDepth; // tracks nesting in (), [], {}
    private bool _readingBlockLabels;

    /// <summary>
    /// Initializes a new reader over the given HCL input.
    /// </summary>
    /// <param name="hclData">The UTF-8 encoded HCL source.</param>
    /// <param name="options">Reader options. Uses defaults if not specified.</param>
    public Utf8HclReader(ReadOnlySpan<byte> hclData, HclReaderOptions options = default)
    {
        _buffer = hclData;
        // default(HclReaderOptions) zero-initializes all fields, bypassing the
        // parameterless constructor. Detect this and apply real defaults.
        _options = options.MaxDepth == 0 ? new HclReaderOptions() : options;
        _isFinalBlock = true;
        _position = 0;
        _line = 1;
        _column = 1;
        _tokenStart = 0;
        _tokenStartLine = 1;
        _tokenStartColumn = 1;
        _currentDepth = 0;
        _tokenType = HclTokenType.None;
        _valueSpan = default;
        _stringStyle = StringLiteralStyle.Quoted;
        _commentMarker = 0;
        _hasValue = false;
        _inBody = true;
        _expectingValue = false;
        _expressionDepth = 0;
        _readingBlockLabels = false;
    }

    /// <summary>
    /// Initializes a new reader for streaming scenarios.
    /// </summary>
    /// <param name="hclData">The current UTF-8 buffer segment.</param>
    /// <param name="isFinalBlock">Whether this is the last segment.</param>
    /// <param name="state">State from a previous reader instance.</param>
    public Utf8HclReader(ReadOnlySpan<byte> hclData, bool isFinalBlock, HclReaderState state)
    {
        _buffer = hclData;
        _options = state.Options;
        _isFinalBlock = isFinalBlock;
        _position = 0;
        _line = state.Line;
        _column = state.Column;
        _tokenStart = 0;
        _tokenStartLine = state.Line;
        _tokenStartColumn = state.Column;
        _currentDepth = state.CurrentDepth;
        _tokenType = HclTokenType.None;
        _valueSpan = default;
        _stringStyle = StringLiteralStyle.Quoted;
        _commentMarker = 0;
        _hasValue = false;
        _inBody = true;
        _expectingValue = false;
        _expressionDepth = 0;
        _readingBlockLabels = false;
    }

    /// <summary>Gets the type of the current token.</summary>
    public HclTokenType TokenType => _tokenType;

    /// <summary>Gets the current block nesting depth.</summary>
    public int CurrentDepth => _currentDepth;

    /// <summary>Gets the total number of bytes consumed.</summary>
    public long BytesConsumed => _position;

    /// <summary>Gets the position at the start of the current token.</summary>
    public Mark TokenStart => new(_tokenStart, _tokenStartLine, _tokenStartColumn);

    /// <summary>Gets the current read position.</summary>
    public Mark Position => new(_position, _line, _column);

    /// <summary>Gets the raw bytes of the current token value.</summary>
    public ReadOnlySpan<byte> ValueSpan => _valueSpan;

    /// <summary>Gets the string literal style for string tokens.</summary>
    public StringLiteralStyle StringStyle => _stringStyle;

    /// <summary>
    /// Gets the leading byte of the current comment marker.
    /// <c>(byte)'#'</c> for hash comments, <c>(byte)'/'</c> for <c>//</c> line comments,
    /// <c>(byte)'*'</c> for <c>/* */</c> block comments.
    /// Only meaningful when <see cref="TokenType"/> is <see cref="HclTokenType.Comment"/>.
    /// </summary>
    public byte CommentMarker => _commentMarker;

    /// <summary>Gets whether this is the final block of input.</summary>
    public bool IsFinalBlock => _isFinalBlock;

    /// <summary>Gets the current reader state for streaming resume.</summary>
    public HclReaderState CurrentState => new()
    {
        CurrentDepth = _currentDepth,
        BytesConsumed = _position,
        Line = _line,
        Column = _column,
        Options = _options,
    };

    /// <summary>
    /// Gets the current token value as a <see cref="string"/>.
    /// </summary>
    /// <returns>The string value, or <c>null</c> for null literals.</returns>
    public string? GetString()
    {
        if (_tokenType == HclTokenType.NullLiteral)
        {
            return null;
        }

        if (_tokenType == HclTokenType.StringLiteral)
        {
            return DecodeStringValue(_valueSpan);
        }

        return Encoding.UTF8.GetString(_valueSpan);
    }

    /// <summary>Gets the current token value as an <see cref="int"/>.</summary>
    public int GetInt32() => int.Parse(Encoding.UTF8.GetString(_valueSpan), CultureInfo.InvariantCulture);

    /// <summary>Gets the current token value as a <see cref="long"/>.</summary>
    public long GetInt64() => long.Parse(Encoding.UTF8.GetString(_valueSpan), CultureInfo.InvariantCulture);

    /// <summary>Gets the current token value as a <see cref="double"/>.</summary>
    public double GetDouble() => double.Parse(Encoding.UTF8.GetString(_valueSpan), CultureInfo.InvariantCulture);

    /// <summary>Gets the current token value as a <see cref="bool"/>.</summary>
    public bool GetBoolean()
    {
        if (_valueSpan.Length == 4 && _valueSpan[0] == (byte)'t')
        {
            return true;
        }

        return false;
    }

    /// <summary>Gets the current token value as a <see cref="decimal"/>.</summary>
    public decimal GetDecimal() => decimal.Parse(Encoding.UTF8.GetString(_valueSpan), CultureInfo.InvariantCulture);
}
