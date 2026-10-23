using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Validation;

/// <summary>
/// Rewrites a boolean condition into an equivalent form that only nests <c>&amp;&amp;</c>/<c>||</c> over
/// atoms, so the recognisers never have to deal with the many spellings of the same rule.
/// <list type="bullet">
/// <item><description><c>!(a &amp;&amp; b)</c> and <c>!(a || b)</c> become <c>!a || !b</c> and <c>!a &amp;&amp; !b</c>.</description></item>
/// <item><description><c>!(a == b)</c> becomes <c>a != b</c>, and likewise for the ordering operators.</description></item>
/// <item><description><c>p ? x : true</c> becomes <c>!p || x</c>; <c>p ? true : x</c> becomes <c>p || x</c>.</description></item>
/// </list>
/// Atoms are never modified; a node that is not affected is returned as-is.
/// </summary>
internal static class ExpressionNormalizer
{
    public static HclExpression Normalize(HclExpression expression)
    {
        switch (expression)
        {
            case HclUnaryExpression { Operator: HclUnaryOperator.Not } not:
                return PushNot(not.Operand);

            case HclBinaryExpression { Operator: HclBinaryOperator.And or HclBinaryOperator.Or } binary:
                return Binary(binary.Operator, Normalize(binary.Left), Normalize(binary.Right));

            case HclConditionalExpression conditional when ExpressionAnalysis.IsBoolLiteral(conditional.FalseResult, true):
                return Binary(HclBinaryOperator.Or, PushNot(conditional.Condition), Normalize(conditional.TrueResult));

            case HclConditionalExpression conditional when ExpressionAnalysis.IsBoolLiteral(conditional.TrueResult, true):
                return Binary(HclBinaryOperator.Or, Normalize(conditional.Condition), Normalize(conditional.FalseResult));

            default:
                return expression;
        }
    }

    /// <summary>Returns the normalised form of <c>!expression</c>.</summary>
    private static HclExpression PushNot(HclExpression expression)
    {
        switch (expression)
        {
            case HclUnaryExpression { Operator: HclUnaryOperator.Not } not:
                return Normalize(not.Operand);

            case HclBinaryExpression { Operator: HclBinaryOperator.And } and:
                return Binary(HclBinaryOperator.Or, PushNot(and.Left), PushNot(and.Right));

            case HclBinaryExpression { Operator: HclBinaryOperator.Or } or:
                return Binary(HclBinaryOperator.And, PushNot(or.Left), PushNot(or.Right));

            case HclBinaryExpression binary when TryInvert(binary.Operator, out var inverted):
                return Binary(inverted, binary.Left, binary.Right);

            case HclLiteralExpression { Kind: HclLiteralKind.Bool } literal:
                return new HclLiteralExpression
                {
                    Kind = HclLiteralKind.Bool,
                    Value = string.Equals(literal.Value, "true", StringComparison.OrdinalIgnoreCase) ? "false" : "true",
                };

            case HclConditionalExpression conditional:
                var normalized = Normalize(conditional);

                // Only recurse when normalisation rewrote the conditional, otherwise we would loop.
                return ReferenceEquals(normalized, conditional)
                    ? Negate(expression)
                    : PushNot(normalized);

            default:
                return Negate(expression);
        }
    }

    private static HclUnaryExpression Negate(HclExpression operand) => new()
    {
        Operator = HclUnaryOperator.Not,
        Operand = operand,
    };

    private static HclBinaryExpression Binary(HclBinaryOperator op, HclExpression left, HclExpression right) => new()
    {
        Left = left,
        Operator = op,
        Right = right,
    };

    private static bool TryInvert(HclBinaryOperator op, out HclBinaryOperator inverted)
    {
        (var isComparison, inverted) = op switch
        {
            HclBinaryOperator.Equal => (true, HclBinaryOperator.NotEqual),
            HclBinaryOperator.NotEqual => (true, HclBinaryOperator.Equal),
            HclBinaryOperator.LessThan => (true, HclBinaryOperator.GreaterEqual),
            HclBinaryOperator.GreaterEqual => (true, HclBinaryOperator.LessThan),
            HclBinaryOperator.GreaterThan => (true, HclBinaryOperator.LessEqual),
            HclBinaryOperator.LessEqual => (true, HclBinaryOperator.GreaterThan),
            _ => (false, op),
        };

        return isComparison;
    }
}
