using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;

namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Thrown when an HCL expression cannot be resolved during evaluation,
/// typically because a referenced variable is not defined in the evaluation context.
/// </summary>
/// <example>
/// <code>
/// try
/// {
///     var result = evaluator.Evaluate(expression, context);
/// }
/// catch (HclUnresolvableException ex)
/// {
///     Console.WriteLine($"Variable '{ex.VariableName}' is not defined.");
/// }
/// </code>
/// </example>
public sealed class HclUnresolvableException : HclException
{
    /// <summary>
    /// Gets the name of the variable or reference that could not be resolved.
    /// </summary>
    public string? VariableName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclUnresolvableException"/> class.
    /// </summary>
    /// <param name="message">A description of why the expression could not be resolved.</param>
    public HclUnresolvableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclUnresolvableException"/> class
    /// with source position information.
    /// </summary>
    /// <param name="message">A description of why the expression could not be resolved.</param>
    /// <param name="position">The source position where the unresolvable reference occurs.</param>
    public HclUnresolvableException(string message, Mark position)
        : base(message, position)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HclUnresolvableException"/> class
    /// with the name of the unresolvable variable and source position.
    /// </summary>
    /// <param name="variableName">The name of the variable that could not be resolved.</param>
    /// <param name="message">A description of why the expression could not be resolved.</param>
    /// <param name="position">The source position where the unresolvable reference occurs.</param>
    public HclUnresolvableException(string variableName, string message, Mark position)
        : base(message, position)
    {
        VariableName = variableName;
    }
}
