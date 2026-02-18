using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorOperationTests
{
    private readonly HclEvaluator _evaluator = new();
    private readonly HclEvaluationContext _context = new();

    private static HclLiteralExpression Num(double v) =>
        new() { Value = v.ToString(System.Globalization.CultureInfo.InvariantCulture), Kind = HclLiteralKind.Number };

    private static HclLiteralExpression Bool(bool v) =>
        new() { Value = v ? "true" : "false", Kind = HclLiteralKind.Bool };

    private static HclLiteralExpression Str(string v) =>
        new() { Value = v, Kind = HclLiteralKind.String };

    [Fact]
    public void Addition()
    {
        var expr = new HclBinaryExpression { Left = Num(1), Operator = HclBinaryOperator.Add, Right = Num(2) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(3.0, result.NumberValue);
    }

    [Fact]
    public void Subtraction()
    {
        var expr = new HclBinaryExpression { Left = Num(10), Operator = HclBinaryOperator.Subtract, Right = Num(3) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(7.0, result.NumberValue);
    }

    [Fact]
    public void Multiplication()
    {
        var expr = new HclBinaryExpression { Left = Num(4), Operator = HclBinaryOperator.Multiply, Right = Num(5) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(20.0, result.NumberValue);
    }

    [Fact]
    public void Division()
    {
        var expr = new HclBinaryExpression { Left = Num(10), Operator = HclBinaryOperator.Divide, Right = Num(3) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(10.0 / 3.0, result.NumberValue, precision: 10);
    }

    [Fact]
    public void Modulo()
    {
        var expr = new HclBinaryExpression { Left = Num(10), Operator = HclBinaryOperator.Modulo, Right = Num(3) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(1.0, result.NumberValue);
    }

    [Fact]
    public void LessThan()
    {
        var expr = new HclBinaryExpression { Left = Num(1), Operator = HclBinaryOperator.LessThan, Right = Num(2) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.True(result.BoolValue);
    }

    [Fact]
    public void GreaterThanFalse()
    {
        var expr = new HclBinaryExpression { Left = Num(1), Operator = HclBinaryOperator.GreaterThan, Right = Num(2) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.False(result.BoolValue);
    }

    [Fact]
    public void LessEqual()
    {
        var expr = new HclBinaryExpression { Left = Num(3), Operator = HclBinaryOperator.LessEqual, Right = Num(3) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.True(result.BoolValue);
    }

    [Fact]
    public void GreaterEqual()
    {
        var expr = new HclBinaryExpression { Left = Num(5), Operator = HclBinaryOperator.GreaterEqual, Right = Num(3) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.True(result.BoolValue);
    }

    [Fact]
    public void EqualStrings()
    {
        var expr = new HclBinaryExpression { Left = Str("a"), Operator = HclBinaryOperator.Equal, Right = Str("a") };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.True(result.BoolValue);
    }

    [Fact]
    public void NotEqualStrings()
    {
        var expr = new HclBinaryExpression { Left = Str("a"), Operator = HclBinaryOperator.NotEqual, Right = Str("b") };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.True(result.BoolValue);
    }

    [Fact]
    public void EqualNumbers()
    {
        var expr = new HclBinaryExpression { Left = Num(42), Operator = HclBinaryOperator.Equal, Right = Num(42) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.True(result.BoolValue);
    }

    [Fact]
    public void LogicalAnd()
    {
        var expr = new HclBinaryExpression { Left = Bool(true), Operator = HclBinaryOperator.And, Right = Bool(false) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.False(result.BoolValue);
    }

    [Fact]
    public void LogicalOr()
    {
        var expr = new HclBinaryExpression { Left = Bool(false), Operator = HclBinaryOperator.Or, Right = Bool(true) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.True(result.BoolValue);
    }

    [Fact]
    public void LogicalNot()
    {
        var expr = new HclUnaryExpression { Operator = HclUnaryOperator.Not, Operand = Bool(true) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.False(result.BoolValue);
    }

    [Fact]
    public void NumericNegation()
    {
        var expr = new HclUnaryExpression { Operator = HclUnaryOperator.Negate, Operand = Num(5) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(-5.0, result.NumberValue);
    }

    [Fact]
    public void ArithmeticOnNonNumbersThrows()
    {
        var expr = new HclBinaryExpression { Left = Str("a"), Operator = HclBinaryOperator.Add, Right = Num(1) };

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(expr, _context));
    }

    [Fact]
    public void ComparisonOnNonNumbersThrows()
    {
        var expr = new HclBinaryExpression { Left = Str("a"), Operator = HclBinaryOperator.LessThan, Right = Str("b") };

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(expr, _context));
    }

    [Fact]
    public void NegateNonNumberThrows()
    {
        var expr = new HclUnaryExpression { Operator = HclUnaryOperator.Negate, Operand = Bool(true) };

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(expr, _context));
    }

    [Fact]
    public void NotOnNonBoolThrows()
    {
        var expr = new HclUnaryExpression { Operator = HclUnaryOperator.Not, Operand = Num(1) };

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(expr, _context));
    }

    [Fact]
    public void UnknownPropagatesThroughBinary()
    {
        var unknown = new HclFunctionCallExpression { Name = "func" };
        var expr = new HclBinaryExpression { Left = unknown, Operator = HclBinaryOperator.Add, Right = Num(1) };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void UnknownPropagatesThroughUnary()
    {
        var unknown = new HclFunctionCallExpression { Name = "func" };
        var expr = new HclUnaryExpression { Operator = HclUnaryOperator.Negate, Operand = unknown };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }
}
