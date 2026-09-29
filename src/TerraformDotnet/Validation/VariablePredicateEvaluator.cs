namespace TerraformDotnet.Validation;

/// <summary>
/// Evaluates a <see cref="VariablePredicate"/> against the current values of a module's variables, using
/// three-valued logic: a result of <c>null</c> means "cannot be decided yet".
/// <para>
/// Typical use: decide whether a <see cref="RequiredWhenConstraint"/> or <see cref="ConditionalConstraint"/>
/// currently applies while the user is still filling in a form.
/// </para>
/// <example>
/// <code>
/// // condition = var.sla != "gold" || var.backup_vault_id != null   (on backup_vault_id)
/// var required = Assert.IsType&lt;RequiredWhenConstraint&gt;(constraints.Single());
///
/// var values = new Dictionary&lt;string, TerraformLiteral&gt; { ["sla"] = TerraformLiteral.FromString("gold") };
/// bool? isRequired = VariablePredicateEvaluator.Evaluate(required.Predicate, values); // true
/// </code>
/// </example>
/// </summary>
public static class VariablePredicateEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="predicate"/>.
    /// <list type="bullet">
    /// <item><description>A variable missing from <paramref name="values"/> is unknown; unknowns propagate the way Terraform's unknown values do: <c>false &amp;&amp; unknown</c> is <c>false</c>, <c>true || unknown</c> is <c>true</c>, everything else stays unknown.</description></item>
    /// <item><description>Pass <see cref="TerraformLiteral.Null"/> for a variable that is known to be <c>null</c>.</description></item>
    /// <item><description>Equality follows Terraform: values of different types are never equal. Ordering comparisons are only defined for numbers and are unknown otherwise.</description></item>
    /// </list>
    /// </summary>
    /// <param name="predicate">The predicate to evaluate.</param>
    /// <param name="values">The known variable values by variable name (without the <c>var.</c> prefix).</param>
    /// <returns><c>true</c> or <c>false</c> when decidable, otherwise <c>null</c>.</returns>
    public static bool? Evaluate(VariablePredicate predicate, IReadOnlyDictionary<string, TerraformLiteral> values)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(values);

        return predicate switch
        {
            ComparePredicate compare => EvaluateCompare(compare, values),
            InPredicate contains => EvaluateIn(contains, values),
            IsNullPredicate isNull => values.TryGetValue(isNull.Variable, out var actual)
                ? actual.IsNull != isNull.Negated
                : null,
            AndPredicate and => EvaluateAnd(and, values),
            OrPredicate or => EvaluateOr(or, values),
            _ => throw new ArgumentOutOfRangeException(nameof(predicate), predicate, "Unknown predicate type."),
        };
    }

    private static bool? EvaluateCompare(ComparePredicate compare, IReadOnlyDictionary<string, TerraformLiteral> values)
    {
        if (!values.TryGetValue(compare.Variable, out var actual))
        {
            return null;
        }

        if (compare.Operator is CompareOperator.Equal or CompareOperator.NotEqual)
        {
            return actual.TerraformEquals(compare.Value) == (compare.Operator == CompareOperator.Equal);
        }

        if (!actual.TryGetNumber(out var left) || !compare.Value.TryGetNumber(out var right))
        {
            return null;
        }

        return compare.Operator switch
        {
            CompareOperator.LessThan => left < right,
            CompareOperator.LessThanOrEqual => left <= right,
            CompareOperator.GreaterThan => left > right,
            CompareOperator.GreaterThanOrEqual => left >= right,
            _ => null,
        };
    }

    private static bool? EvaluateIn(InPredicate contains, IReadOnlyDictionary<string, TerraformLiteral> values)
    {
        if (!values.TryGetValue(contains.Variable, out var actual))
        {
            return null;
        }

        return contains.Values.Any(actual.TerraformEquals) != contains.Negated;
    }

    private static bool? EvaluateAnd(AndPredicate and, IReadOnlyDictionary<string, TerraformLiteral> values)
    {
        var unknown = false;

        foreach (var operand in and.Operands)
        {
            switch (Evaluate(operand, values))
            {
                case false:
                    return false;
                case null:
                    unknown = true;
                    break;
            }
        }

        return unknown ? null : true;
    }

    private static bool? EvaluateOr(OrPredicate or, IReadOnlyDictionary<string, TerraformLiteral> values)
    {
        var unknown = false;

        foreach (var operand in or.Operands)
        {
            switch (Evaluate(operand, values))
            {
                case true:
                    return true;
                case null:
                    unknown = true;
                    break;
            }
        }

        return unknown ? null : false;
    }
}
