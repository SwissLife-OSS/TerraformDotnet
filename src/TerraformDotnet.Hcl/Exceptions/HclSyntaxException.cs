using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Exceptions;

/// <summary>
/// Exception thrown when the HCL input contains a syntax error.
/// Always includes the source position where the error was detected.
/// </summary>
public sealed class HclSyntaxException : HclException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HclSyntaxException"/> class.
    /// </summary>
    /// <param name="message">A description of the syntax error.</param>
    /// <param name="position">The source position where the error was detected.</param>
    public HclSyntaxException(string message, Mark position)
        : base(message, position)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclSyntaxException"/> class with a path.
    /// </summary>
    /// <param name="message">A description of the syntax error.</param>
    /// <param name="position">The source position where the error was detected.</param>
    /// <param name="path">The logical path to the element.</param>
    public HclSyntaxException(string message, Mark position, string path)
        : base(message, position, path)
    {
    }
}
