using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a <c>validation { ... }</c> block inside a variable declaration.
/// <example>
/// <code>
/// // validation {
/// //   condition     = var.retention >= 1 &amp;&amp; var.retention &lt;= 35
/// //   error_message = "Must be between 1 and 35."
/// // }
/// </code>
/// </example>
/// </summary>
public sealed class TerraformValidation
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformValidation"/>.
    /// </summary>
    /// <param name="condition">The condition expression.</param>
    /// <param name="errorMessage">The error message.</param>
    internal TerraformValidation(HclExpression condition, string errorMessage)
    {
        Condition = condition;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets the condition expression.</summary>
    public HclExpression Condition { get; }

    /// <summary>Gets the error message.</summary>
    public string ErrorMessage { get; }
}
