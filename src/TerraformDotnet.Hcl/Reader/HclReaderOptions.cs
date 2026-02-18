namespace TerraformDotnet.Hcl.Reader;

/// <summary>
/// Options that control the behavior of <see cref="Utf8HclReader"/>.
/// </summary>
public readonly struct HclReaderOptions
{
    /// <summary>
    /// Gets the maximum nesting depth allowed before throwing
    /// <see cref="Exceptions.MaxRecursionDepthExceededException"/>. Default is 64.
    /// </summary>
    public int MaxDepth { get; init; } = 64;

    /// <summary>
    /// Gets a value indicating whether comments should be emitted as tokens.
    /// When <c>false</c> (default), comments are silently skipped.
    /// </summary>
    public bool ReadComments { get; init; } = false;

    /// <summary>
    /// Initializes a new <see cref="HclReaderOptions"/> with default values.
    /// </summary>
    public HclReaderOptions()
    {
    }
}
