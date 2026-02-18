using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Exceptions;

/// <summary>
/// Exception thrown when the HCL input is syntactically valid but contains a semantic error,
/// such as duplicate attribute names within the same body.
/// </summary>
public sealed class HclSemanticException : HclException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HclSemanticException"/> class.
    /// </summary>
    /// <param name="message">A description of the semantic error.</param>
    /// <param name="position">The source position where the error was detected.</param>
    public HclSemanticException(string message, Mark position)
        : base(message, position)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclSemanticException"/> class with a path.
    /// </summary>
    /// <param name="message">A description of the semantic error.</param>
    /// <param name="position">The source position where the error was detected.</param>
    /// <param name="path">The logical path to the element.</param>
    public HclSemanticException(string message, Mark position, string path)
        : base(message, position, path)
    {
    }
}
