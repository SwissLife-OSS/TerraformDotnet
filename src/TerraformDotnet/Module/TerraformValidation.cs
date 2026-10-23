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
    /// <param name="errorMessage">The error message text.</param>
    /// <param name="errorMessageExpression">The <c>error_message</c> expression.</param>
    internal TerraformValidation(HclExpression condition, string errorMessage, HclExpression errorMessageExpression)
    {
        Condition = condition;
        ErrorMessage = errorMessage;
        ErrorMessageExpression = errorMessageExpression;
    }

    /// <summary>Gets the condition expression.</summary>
    public HclExpression Condition { get; }

    /// <summary>
    /// Gets the error message text. For a literal <c>error_message</c> this is the text as written
    /// (including any <c>${...}</c> interpolations); for any other expression it is the HCL source
    /// of that expression. Use <see cref="ErrorMessageExpression"/> to evaluate it.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Gets the <c>error_message</c> expression. It is usually a string literal, but Terraform also
    /// allows interpolations and function calls such as <c>format(...)</c>.
    /// </summary>
    public HclExpression ErrorMessageExpression { get; }
}
