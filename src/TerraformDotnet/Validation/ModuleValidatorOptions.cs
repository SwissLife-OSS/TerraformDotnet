using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Validation;

/// <summary>Configures a <see cref="ModuleValidator"/>.</summary>
public sealed class ModuleValidatorOptions
{
    /// <summary>
    /// Gets the resolver for function calls. Defaults to <see cref="TerraformDotnet.Evaluation.TerraformFunctions.Default"/>.
    /// Functions the resolver does not support make a validation indeterminate.
    /// </summary>
    public IHclFunctionResolver? FunctionResolver { get; init; }

    /// <summary>Gets the maximum expression nesting depth. Defaults to 128.</summary>
    public int MaxDepth { get; init; } = 128;

    /// <summary>
    /// Gets the maximum number of <c>for</c> expression iterations per evaluated expression.
    /// Defaults to 100,000. Exceeding it makes the validation indeterminate.
    /// </summary>
    public int MaxIterations { get; init; } = 100_000;
}
