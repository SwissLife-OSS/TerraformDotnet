namespace TerraformDotnet.Emit;

/// <summary>
/// Represents a value in a <c>.tfvars</c> file, optionally with an inline comment.
/// Implicit conversion from <see cref="string"/> allows plain values without wrapping.
/// <example>
/// <code>
/// InputValue val1 = new("\"hello\"", "Greeting");
/// InputValue val2 = "\"world\""; // implicit, no comment
/// </code>
/// </example>
/// </summary>
public sealed class InputValue
{
    /// <summary>
    /// Initializes a new instance of <see cref="InputValue"/>.
    /// </summary>
    /// <param name="expression">The raw HCL expression (e.g. "\"hello\"", "42", "true").</param>
    /// <param name="comment">Optional inline comment emitted after the value.</param>
    public InputValue(string expression, string? comment = null)
    {
        Expression = expression;
        Comment = comment;
    }

    /// <summary>Gets the raw HCL expression (e.g. "\"hello\"", "42", "true").</summary>
    public string Expression { get; }

    /// <summary>Gets the optional inline comment emitted after the value (e.g. "# Comment").</summary>
    public string? Comment { get; }

    /// <summary>
    /// Allows implicit conversion: <c>["key"] = "value"</c> without wrapping in <see cref="InputValue"/>.
    /// </summary>
    /// <param name="expression">The raw HCL expression.</param>
    public static implicit operator InputValue(string expression) => new(expression);
}
