namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Configures the behavior of an <see cref="HclEvaluator"/>.
/// </summary>
/// <example>
/// <code>
/// var evaluator = new HclEvaluator(new HclEvaluatorOptions
/// {
///     FunctionResolver = new MyFunctions(),
///     TreatUndefinedVariablesAsUnknown = true,
/// });
/// </code>
/// </example>
public sealed class HclEvaluatorOptions
{
    /// <summary>
    /// Gets the resolver used to evaluate function calls. When <c>null</c> every function call
    /// (other than <c>can</c> and <c>try</c>) evaluates to <see cref="HclValueType.Unknown"/>.
    /// </summary>
    public IHclFunctionResolver? FunctionResolver { get; init; }

    /// <summary>
    /// Gets a value indicating whether a reference to a root name that is not bound in the
    /// evaluation context (for example <c>data</c>, <c>path</c> or a resource address) evaluates
    /// to <see cref="HclValueType.Unknown"/> instead of throwing <see cref="HclUnresolvableException"/>.
    /// </summary>
    public bool TreatUndefinedVariablesAsUnknown { get; init; }

    /// <summary>Gets the maximum expression nesting depth. Defaults to 128.</summary>
    public int MaxDepth { get; init; } = 128;

    /// <summary>
    /// Gets the maximum number of <c>for</c> iterations performed by a single
    /// <see cref="HclEvaluator.Evaluate"/> call. Defaults to 100,000.
    /// </summary>
    public int MaxIterations { get; init; } = 100_000;
}
