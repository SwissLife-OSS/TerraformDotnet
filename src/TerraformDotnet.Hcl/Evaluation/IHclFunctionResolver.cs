namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Resolves function calls encountered by the <see cref="HclEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator only invokes the resolver with fully known arguments — a call that has at least
/// one <see cref="HclValueType.Unknown"/> argument short-circuits to an unknown result without
/// reaching the resolver. The lazily evaluated functions <c>can</c> and <c>try</c> are handled by
/// the evaluator itself and never reach the resolver either.
/// </para>
/// <para>
/// Implementations signal "this function is not supported" by returning <c>null</c> (the evaluator
/// then produces an unknown value) and signal "the arguments are invalid" by throwing
/// <see cref="HclFunctionException"/> (which <c>can</c> and <c>try</c> can recover from).
/// </para>
/// </remarks>
public interface IHclFunctionResolver
{
    /// <summary>Invokes the named function.</summary>
    /// <param name="name">The function name as written in the expression (may include a provider namespace).</param>
    /// <param name="arguments">The evaluated, fully known arguments.</param>
    /// <returns>The result, or <c>null</c> when the function is not supported and the call should stay unknown.</returns>
    /// <exception cref="HclFunctionException">Thrown when the arguments are invalid for the function.</exception>
    HclValue? Invoke(string name, IReadOnlyList<HclValue> arguments);
}
