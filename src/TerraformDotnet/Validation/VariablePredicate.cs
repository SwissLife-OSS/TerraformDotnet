namespace TerraformDotnet.Validation;

/// <summary>
/// A small, closed and serialisable boolean model over the values of <em>other</em> variables. It is
/// the "when" part of <see cref="RequiredWhenConstraint"/> and <see cref="ConditionalConstraint"/>,
/// and can be evaluated with <see cref="VariablePredicateEvaluator"/>.
/// <para>
/// Negation is folded into the leaves (<see cref="CompareOperator"/>, <c>Negated</c> flags and
/// De&#160;Morgan on <see cref="AndPredicate"/>/<see cref="OrPredicate"/>), so there is no
/// <c>NotPredicate</c>.
/// </para>
/// </summary>
public abstract record VariablePredicate
{
    private protected VariablePredicate()
    {
    }
}

/// <summary>
/// Compares a variable with a literal, e.g. <c>var.sla == "gold"</c>.
/// </summary>
/// <param name="Variable">The variable name (without the <c>var.</c> prefix).</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Value">The literal to compare with.</param>
public sealed record ComparePredicate(string Variable, CompareOperator Operator, TerraformLiteral Value)
    : VariablePredicate;

/// <summary>
/// Tests whether a variable is one of a set of literals, e.g. <c>contains(["gold", "platinum"], var.sla)</c>.
/// </summary>
/// <param name="Variable">The variable name (without the <c>var.</c> prefix).</param>
/// <param name="Values">The literals to look for.</param>
/// <param name="Negated"><c>true</c> when the variable must <em>not</em> be one of the values.</param>
public sealed record InPredicate(string Variable, IReadOnlyList<TerraformLiteral> Values, bool Negated = false)
    : VariablePredicate;

/// <summary>
/// Tests whether a variable is <c>null</c>, e.g. <c>var.vault == null</c>.
/// </summary>
/// <param name="Variable">The variable name (without the <c>var.</c> prefix).</param>
/// <param name="Negated"><c>true</c> for <c>var.x != null</c>.</param>
public sealed record IsNullPredicate(string Variable, bool Negated = false) : VariablePredicate;

/// <summary>All operands must hold (<c>&amp;&amp;</c>).</summary>
/// <param name="Operands">The operands.</param>
public sealed record AndPredicate(IReadOnlyList<VariablePredicate> Operands) : VariablePredicate;

/// <summary>At least one operand must hold (<c>||</c>).</summary>
/// <param name="Operands">The operands.</param>
public sealed record OrPredicate(IReadOnlyList<VariablePredicate> Operands) : VariablePredicate;
