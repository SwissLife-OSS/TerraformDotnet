using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorConditionalTests
{
    private readonly HclEvaluator _evaluator = new();
    private readonly HclEvaluationContext _context = new();

    [Fact]
    public void TrueConditionReturnsTrue()
    {
        var expr = new HclConditionalExpression
        {
            Condition = new HclLiteralExpression { Value = "true", Kind = HclLiteralKind.Bool },
            TrueResult = new HclLiteralExpression { Value = "yes", Kind = HclLiteralKind.String },
            FalseResult = new HclLiteralExpression { Value = "no", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal("yes", result.StringValue);
    }

    [Fact]
    public void FalseConditionReturnsFalse()
    {
        var expr = new HclConditionalExpression
        {
            Condition = new HclLiteralExpression { Value = "false", Kind = HclLiteralKind.Bool },
            TrueResult = new HclLiteralExpression { Value = "yes", Kind = HclLiteralKind.String },
            FalseResult = new HclLiteralExpression { Value = "no", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal("no", result.StringValue);
    }

    [Fact]
    public void ConditionalWithVariablePredicate()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("enabled", HclValue.FromBool(true));

        var expr = new HclConditionalExpression
        {
            Condition = new HclVariableExpression { Name = "enabled" },
            TrueResult = new HclLiteralExpression { Value = "on", Kind = HclLiteralKind.String },
            FalseResult = new HclLiteralExpression { Value = "off", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal("on", result.StringValue);
    }

    [Fact]
    public void NestedConditionals()
    {
        // true ? (false ? "a" : "b") : "c" → "b"
        var expr = new HclConditionalExpression
        {
            Condition = new HclLiteralExpression { Value = "true", Kind = HclLiteralKind.Bool },
            TrueResult = new HclConditionalExpression
            {
                Condition = new HclLiteralExpression { Value = "false", Kind = HclLiteralKind.Bool },
                TrueResult = new HclLiteralExpression { Value = "a", Kind = HclLiteralKind.String },
                FalseResult = new HclLiteralExpression { Value = "b", Kind = HclLiteralKind.String },
            },
            FalseResult = new HclLiteralExpression { Value = "c", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal("b", result.StringValue);
    }

    [Fact]
    public void ConditionalWithNonBoolConditionThrows()
    {
        var expr = new HclConditionalExpression
        {
            Condition = new HclLiteralExpression { Value = "42", Kind = HclLiteralKind.Number },
            TrueResult = new HclLiteralExpression { Value = "yes", Kind = HclLiteralKind.String },
            FalseResult = new HclLiteralExpression { Value = "no", Kind = HclLiteralKind.String },
        };

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(expr, _context));
    }

    [Fact]
    public void ConditionalWithUnknownConditionPropagates()
    {
        var expr = new HclConditionalExpression
        {
            Condition = new HclFunctionCallExpression { Name = "can" },
            TrueResult = new HclLiteralExpression { Value = "yes", Kind = HclLiteralKind.String },
            FalseResult = new HclLiteralExpression { Value = "no", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void ConditionalShortCircuitsOnTrue()
    {
        // The false branch references an undefined variable, but should not be evaluated
        var ctx = new HclEvaluationContext();
        var expr = new HclConditionalExpression
        {
            Condition = new HclLiteralExpression { Value = "true", Kind = HclLiteralKind.Bool },
            TrueResult = new HclLiteralExpression { Value = "safe", Kind = HclLiteralKind.String },
            FalseResult = new HclVariableExpression { Name = "undefined" },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal("safe", result.StringValue);
    }

    [Fact]
    public void ConditionalShortCircuitsOnFalse()
    {
        // The true branch references an undefined variable, but should not be evaluated
        var ctx = new HclEvaluationContext();
        var expr = new HclConditionalExpression
        {
            Condition = new HclLiteralExpression { Value = "false", Kind = HclLiteralKind.Bool },
            TrueResult = new HclVariableExpression { Name = "undefined" },
            FalseResult = new HclLiteralExpression { Value = "safe", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal("safe", result.StringValue);
    }
}
