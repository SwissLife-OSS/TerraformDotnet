using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Exceptions;

/// <summary>
/// Base exception for all HCL parsing and processing errors.
/// </summary>
public class HclException : Exception
{
    /// <summary>
    /// Gets the position in the source where the error occurred, if available.
    /// </summary>
    public Mark? Position { get; }

    /// <summary>
    /// Gets the logical path to the element that caused the error, if available.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public HclException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclException"/> class with position information.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="position">The source position where the error occurred.</param>
    public HclException(string message, Mark position)
        : base(FormatMessage(message, position))
    {
        Position = position;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclException"/> class with position and path information.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="position">The source position where the error occurred.</param>
    /// <param name="path">The logical path to the element.</param>
    public HclException(string message, Mark position, string path)
        : base(FormatMessage(message, position, path))
    {
        Position = position;
        Path = path;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public HclException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclException"/> class with position and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="position">The source position where the error occurred.</param>
    /// <param name="innerException">The inner exception.</param>
    public HclException(string message, Mark position, Exception innerException)
        : base(FormatMessage(message, position), innerException)
    {
        Position = position;
    }

    private static string FormatMessage(string message, Mark position) =>
        $"{message} {position}";

    private static string FormatMessage(string message, Mark position, string path) =>
        $"{message} at '{path}' {position}";
}
