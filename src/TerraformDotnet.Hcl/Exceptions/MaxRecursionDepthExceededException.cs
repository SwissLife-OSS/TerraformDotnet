using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Exceptions;

/// <summary>
/// Exception thrown when the parser exceeds the maximum allowed nesting depth.
/// This protects against stack overflows from deeply nested or malicious input.
/// </summary>
public sealed class MaxRecursionDepthExceededException : HclException
{
    /// <summary>
    /// Gets the maximum depth that was configured.
    /// </summary>
    public int MaxDepth { get; }

    /// <summary>
    /// Gets the depth at which the limit was exceeded.
    /// </summary>
    public int CurrentDepth { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MaxRecursionDepthExceededException"/> class.
    /// </summary>
    /// <param name="maxDepth">The configured maximum depth.</param>
    /// <param name="currentDepth">The depth at which the limit was exceeded.</param>
    /// <param name="position">The source position where the limit was hit.</param>
    public MaxRecursionDepthExceededException(int maxDepth, int currentDepth, Mark position)
        : base($"Maximum recursion depth of {maxDepth} exceeded (current depth: {currentDepth}).", position)
    {
        MaxDepth = maxDepth;
        CurrentDepth = currentDepth;
    }
}
