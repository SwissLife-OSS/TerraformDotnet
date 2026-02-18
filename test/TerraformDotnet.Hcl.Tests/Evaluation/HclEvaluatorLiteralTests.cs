using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorLiteralTests
{
    private readonly HclEvaluator _evaluator = new();
    private readonly HclEvaluationContext _context = new();

    [Fact]
    public void StringLiteral()
    {
        var expr = new HclLiteralExpression { Value = "hello", Kind = HclLiteralKind.String };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.String, result.Type);
        Assert.Equal("hello", result.StringValue);
    }

    [Fact]
    public void EmptyStringLiteral()
    {
        var expr = new HclLiteralExpression { Value = "", Kind = HclLiteralKind.String };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.String, result.Type);
        Assert.Equal("", result.StringValue);
    }

    [Fact]
    public void NullStringLiteral()
    {
        var expr = new HclLiteralExpression { Value = null, Kind = HclLiteralKind.String };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.String, result.Type);
        Assert.Equal("", result.StringValue);
    }

    [Fact]
    public void IntegerNumberLiteral()
    {
        var expr = new HclLiteralExpression { Value = "42", Kind = HclLiteralKind.Number };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Number, result.Type);
        Assert.Equal(42.0, result.NumberValue);
    }

    [Fact]
    public void FloatingPointNumberLiteral()
    {
        var expr = new HclLiteralExpression { Value = "3.14", Kind = HclLiteralKind.Number };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Number, result.Type);
        Assert.Equal(3.14, result.NumberValue, precision: 10);
    }

    [Fact]
    public void ScientificNotationLiteral()
    {
        var expr = new HclLiteralExpression { Value = "1.5e3", Kind = HclLiteralKind.Number };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Number, result.Type);
        Assert.Equal(1500.0, result.NumberValue);
    }

    [Fact]
    public void BoolTrueLiteral()
    {
        var expr = new HclLiteralExpression { Value = "true", Kind = HclLiteralKind.Bool };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Bool, result.Type);
        Assert.True(result.BoolValue);
    }

    [Fact]
    public void BoolFalseLiteral()
    {
        var expr = new HclLiteralExpression { Value = "false", Kind = HclLiteralKind.Bool };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Bool, result.Type);
        Assert.False(result.BoolValue);
    }

    [Fact]
    public void NullLiteral()
    {
        var expr = new HclLiteralExpression { Value = null, Kind = HclLiteralKind.Null };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Null, result.Type);
        Assert.Same(HclValue.Null, result);
    }

    [Fact]
    public void NegativeNumberLiteral()
    {
        var expr = new HclLiteralExpression { Value = "-7", Kind = HclLiteralKind.Number };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Number, result.Type);
        Assert.Equal(-7.0, result.NumberValue);
    }

    [Fact]
    public void ZeroNumberLiteral()
    {
        var expr = new HclLiteralExpression { Value = "0", Kind = HclLiteralKind.Number };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Number, result.Type);
        Assert.Equal(0.0, result.NumberValue);
    }
}
