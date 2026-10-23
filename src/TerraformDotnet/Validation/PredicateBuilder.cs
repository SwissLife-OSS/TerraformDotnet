using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Validation;

/// <summary>
/// Converts a normalised HCL condition over other variables into a <see cref="VariablePredicate"/>,
/// and negates predicates without introducing a dedicated <c>Not</c> node.
/// </summary>
internal static class PredicateBuilder
{
    /// <summary>
    /// Builds a predicate, or returns <c>null</c> when any part of the expression falls outside the
    /// supported shapes (comparison of a variable with a literal, null checks, <c>contains</c> over a
    /// literal list, boolean variables, and <c>&amp;&amp;</c>/<c>||</c>/<c>!</c> over those).
    /// </summary>
    public static VariablePredicate? TryBuild(HclExpression expression, IReadOnlyDictionary<string, HclExpression> locals)
    {
        switch (expression)
        {
            case HclBinaryExpression { Operator: HclBinaryOperator.And or HclBinaryOperator.Or } logical:
                var isAnd = logical.Operator == HclBinaryOperator.And;
                var parts = isAnd
                    ? ExpressionAnalysis.FlattenAnd(expression)
                    : ExpressionAnalysis.FlattenOr(expression);
                var operands = new List<VariablePredicate>(parts.Count);

                foreach (var part in parts)
                {
                    var operand = TryBuild(part, locals);

                    if (operand is null)
                    {
                        return null;
                    }

                    operands.Add(operand);
                }

                return isAnd ? MakeAnd(operands) : MakeOr(operands);

            case HclBinaryExpression comparison when TryGetCompareOperator(comparison.Operator, out var op):
                return BuildComparison(comparison.Left, op, comparison.Right);

            case HclUnaryExpression { Operator: HclUnaryOperator.Not } not:
                var inner = TryBuild(not.Operand, locals);

                return inner is null ? null : Negate(inner);

            case HclFunctionCallExpression when ExpressionAnalysis.TryGetFunction(expression, "contains", 2, out var args)
                && ExpressionAnalysis.TryGetVariableName(args[1], out var name)
                && ExpressionAnalysis.TryResolveLiteralList(args[0], locals, out var values):
                return new InPredicate(name, values);

            case not null when ExpressionAnalysis.TryGetVariableName(expression, out var boolVariable):
                return new ComparePredicate(boolVariable, CompareOperator.Equal, TerraformLiteral.FromBool(true));

            default:
                return null;
        }
    }

    /// <summary>Returns the logical negation of a predicate, folded into its leaves.</summary>
    public static VariablePredicate Negate(VariablePredicate predicate) => predicate switch
    {
        ComparePredicate compare => compare with { Operator = Invert(compare.Operator) },
        InPredicate contains => contains with { Negated = !contains.Negated },
        IsNullPredicate isNull => isNull with { Negated = !isNull.Negated },
        AndPredicate and => MakeOr([.. and.Operands.Select(Negate)]),
        OrPredicate or => MakeAnd([.. or.Operands.Select(Negate)]),
        _ => throw new ArgumentOutOfRangeException(nameof(predicate), predicate, "Unknown predicate type."),
    };

    public static VariablePredicate MakeAnd(IReadOnlyList<VariablePredicate> operands)
    {
        var flattened = new List<VariablePredicate>(operands.Count);

        foreach (var operand in operands)
        {
            if (operand is AndPredicate nested)
            {
                flattened.AddRange(nested.Operands);
            }
            else
            {
                flattened.Add(operand);
            }
        }

        return flattened.Count == 1 ? flattened[0] : new AndPredicate(flattened);
    }

    public static VariablePredicate MakeOr(IReadOnlyList<VariablePredicate> operands)
    {
        var flattened = new List<VariablePredicate>(operands.Count);

        foreach (var operand in operands)
        {
            if (operand is OrPredicate nested)
            {
                flattened.AddRange(nested.Operands);
            }
            else
            {
                flattened.Add(operand);
            }
        }

        return flattened.Count == 1 ? flattened[0] : new OrPredicate(flattened);
    }

    public static bool TryGetCompareOperator(HclBinaryOperator op, out CompareOperator result)
    {
        (var isComparison, result) = op switch
        {
            HclBinaryOperator.Equal => (true, CompareOperator.Equal),
            HclBinaryOperator.NotEqual => (true, CompareOperator.NotEqual),
            HclBinaryOperator.LessThan => (true, CompareOperator.LessThan),
            HclBinaryOperator.LessEqual => (true, CompareOperator.LessThanOrEqual),
            HclBinaryOperator.GreaterThan => (true, CompareOperator.GreaterThan),
            HclBinaryOperator.GreaterEqual => (true, CompareOperator.GreaterThanOrEqual),
            _ => (false, CompareOperator.Equal),
        };

        return isComparison;
    }

    private static VariablePredicate? BuildComparison(HclExpression left, CompareOperator op, HclExpression right)
    {
        if (ExpressionAnalysis.TryGetVariableName(left, out var leftName)
            && TerraformLiteral.TryFromExpression(right, out var rightLiteral))
        {
            return CreateLeaf(leftName, op, rightLiteral);
        }

        if (ExpressionAnalysis.TryGetVariableName(right, out var rightName)
            && TerraformLiteral.TryFromExpression(left, out var leftLiteral))
        {
            return CreateLeaf(rightName, Swap(op), leftLiteral);
        }

        return null;
    }

    private static VariablePredicate? CreateLeaf(string variable, CompareOperator op, TerraformLiteral literal)
    {
        if (!literal.IsNull)
        {
            return new ComparePredicate(variable, op, literal);
        }

        return op switch
        {
            CompareOperator.Equal => new IsNullPredicate(variable),
            CompareOperator.NotEqual => new IsNullPredicate(variable, Negated: true),
            _ => null,
        };
    }

    /// <summary>Operator to use when the operands of a comparison are swapped.</summary>
    private static CompareOperator Swap(CompareOperator op) => op switch
    {
        CompareOperator.LessThan => CompareOperator.GreaterThan,
        CompareOperator.LessThanOrEqual => CompareOperator.GreaterThanOrEqual,
        CompareOperator.GreaterThan => CompareOperator.LessThan,
        CompareOperator.GreaterThanOrEqual => CompareOperator.LessThanOrEqual,
        _ => op,
    };

    /// <summary>Operator that holds exactly when <paramref name="op"/> does not.</summary>
    private static CompareOperator Invert(CompareOperator op) => op switch
    {
        CompareOperator.Equal => CompareOperator.NotEqual,
        CompareOperator.NotEqual => CompareOperator.Equal,
        CompareOperator.LessThan => CompareOperator.GreaterThanOrEqual,
        CompareOperator.LessThanOrEqual => CompareOperator.GreaterThan,
        CompareOperator.GreaterThan => CompareOperator.LessThanOrEqual,
        CompareOperator.GreaterThanOrEqual => CompareOperator.LessThan,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown compare operator."),
    };
}
