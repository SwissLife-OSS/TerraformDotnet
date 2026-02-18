namespace TerraformDotnet.Hcl.Reader;

/// <summary>
/// Captures the state of a <see cref="Utf8HclReader"/> so reading can be resumed
/// from a new buffer segment (streaming scenario).
/// </summary>
public readonly struct HclReaderState
{
    /// <summary>Gets the current block nesting depth.</summary>
    public int CurrentDepth { get; init; }

    /// <summary>Gets the total number of bytes consumed so far.</summary>
    public long BytesConsumed { get; init; }

    /// <summary>Gets the current one-based line number.</summary>
    public int Line { get; init; }

    /// <summary>Gets the current one-based column number.</summary>
    public int Column { get; init; }

    /// <summary>Gets the reader options in effect.</summary>
    public HclReaderOptions Options { get; init; }
}
