using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorCoercionTests
{
    private static HclValue Evaluate(string expression, params (string Name, HclValue Value)[] variables)
    {
        var context = new HclEvaluationContext();
        foreach (var (name, value) in variables)
        {
            context.SetVariable(name, value);
        }

        return new HclEvaluator().Evaluate(HclExpression.Parse(expression), context);
    }

    [Theory]
    [InlineData("\"5\" < 10", true)]
    [InlineData("10 > \"5\"", true)]
    [InlineData("\"5\" <= \"5\"", true)]
    [InlineData("\"3\" >= 4", false)]
    [InlineData("\"1.5\" < 2", true)]
    [InlineData("\"1e2\" > 99", true)]
    public void ComparisonConvertsNumericStrings(string expression, bool expected)
    {
        Assert.Equal(HclValue.FromBool(expected), Evaluate(expression));
    }

    [Theory]
    [InlineData("\"5\" + 1", 6)]
    [InlineData("2 * \"3\"", 6)]
    [InlineData("-\"5\"", -5)]
    [InlineData("\"10\" % 4", 2)]
    [InlineData("10 / 4", 2.5)]
    public void ArithmeticConvertsNumericStrings(string expression, double expected)
    {
        Assert.Equal(HclValue.FromNumber(expected), Evaluate(expression));
    }

    [Theory]
    [InlineData("\"abc\" < 1")]
    [InlineData("\"a\" < \"b\"")]
    [InlineData("null < 1")]
    [InlineData("true + 1")]
    [InlineData("\" 5\" + 1")]
    [InlineData("\"Infinity\" + 1")]
    [InlineData("5 % 0")]
    public void UnconvertibleOperandsAreRejected(string expression)
    {
        Assert.Throws<InvalidOperationException>(() => Evaluate(expression));
    }

    [Theory]
    [InlineData("1 == \"1\"")]
    [InlineData("true == \"true\"")]
    [InlineData("null == 0")]
    public void EqualityNeverConverts(string expression)
    {
        Assert.Equal(HclValue.False, Evaluate(expression));
    }

    [Fact]
    public void NullEqualsNull()
    {
        Assert.Equal(HclValue.True, Evaluate("x == null", ("x", HclValue.Null)));
    }

    [Fact]
    public void ComparisonWithUnknownIsUnknown()
    {
        var result = Evaluate("u < 1", ("u", HclValue.Unknown("test")));

        Assert.Equal(HclValueType.Unknown, result.Type);
    }
}
