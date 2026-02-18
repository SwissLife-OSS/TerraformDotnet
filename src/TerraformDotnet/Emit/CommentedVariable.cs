namespace TerraformDotnet.Emit;

/// <summary>
/// An optional variable rendered as a comment in the module block.
/// Indicates a variable that can be set but was not explicitly provided.
/// <example>
/// <code>
/// // Rendered in module block as:
/// // # (Optional) Defines which tier to use.
/// // # tier = var.tier
/// </code>
/// </example>
/// </summary>
internal sealed class CommentedVariable
{
    /// <summary>
    /// Initializes a new instance of <see cref="CommentedVariable"/>.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="description">The variable description from the source module.</param>
    /// <param name="suggestedExpression">The suggested HCL expression (e.g. "var.tier").</param>
    internal CommentedVariable(string name, string? description, string suggestedExpression)
    {
        Name = name;
        Description = description;
        SuggestedExpression = suggestedExpression;
    }

    /// <summary>Gets the variable name.</summary>
    public string Name { get; }

    /// <summary>Gets the variable description from the source module.</summary>
    public string? Description { get; }

    /// <summary>Gets the suggested HCL expression (e.g. "var.tier").</summary>
    public string SuggestedExpression { get; }
}
