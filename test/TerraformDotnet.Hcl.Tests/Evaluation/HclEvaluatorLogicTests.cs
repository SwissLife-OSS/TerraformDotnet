using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorLogicTests
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

    private static readonly HclValue Unknown = HclValue.Unknown("test");

    [Theory]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("false && true", false)]
    [InlineData("false && false", false)]
    [InlineData("true || true", true)]
    [InlineData("true || false", true)]
    [InlineData("false || true", true)]
    [InlineData("false || false", false)]
    public void KnownOperandsFollowBooleanLogic(string expression, bool expected)
    {
        var result = Evaluate(expression);

        Assert.Equal(HclValue.FromBool(expected), result);
    }

    [Theory]
    [InlineData("u && false", false)]
    [InlineData("false && u", false)]
    [InlineData("u || true", true)]
    [InlineData("true || u", true)]
    public void DecisiveOperandWinsOverUnknown(string expression, bool expected)
    {
        var result = Evaluate(expression, ("u", Unknown));

        Assert.Equal(HclValue.FromBool(expected), result);
    }

    [Theory]
    [InlineData("u && true")]
    [InlineData("true && u")]
    [InlineData("u && u")]
    [InlineData("u || false")]
    [InlineData("false || u")]
    [InlineData("u || u")]
    [InlineData("!u")]
    public void NonDecisiveUnknownStaysUnknown(string expression)
    {
        var result = Evaluate(expression, ("u", Unknown));

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void AndDoesNotEvaluateRightSideWhenLeftIsFalse()
    {
        var result = Evaluate("false && missing.attribute");

        Assert.Equal(HclValue.False, result);
    }

    [Fact]
    public void OrDoesNotEvaluateRightSideWhenLeftIsTrue()
    {
        var result = Evaluate("true || missing.attribute");

        Assert.Equal(HclValue.True, result);
    }

    [Fact]
    public void GuardedNullAccessDoesNotThrow()
    {
        var result = Evaluate("x != null && x.name == \"a\"", ("x", HclValue.Null));

        Assert.Equal(HclValue.False, result);
    }

    [Fact]
    public void RightSideErrorIsStillRaisedWhenLeftDoesNotDecide()
    {
        Assert.Throws<InvalidOperationException>(() => Evaluate("true && x.name", ("x", HclValue.Null)));
    }

    [Theory]
    [InlineData("\"true\" && true", true)]
    [InlineData("true && \"false\"", false)]
    [InlineData("!\"true\"", false)]
    [InlineData("\"true\" ? 1 : 2", 1)]
    public void BooleanStringsAreConvertedForLogicalOperators(string expression, object expected)
    {
        var result = Evaluate(expression);

        var expectedValue = expected switch
        {
            bool b => HclValue.FromBool(b),
            int n => HclValue.FromNumber(n),
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData("null && true")]
    [InlineData("1 && true")]
    [InlineData("\"yes\" || false")]
    [InlineData("!1")]
    public void NonBooleanOperandsAreRejected(string expression)
    {
        Assert.Throws<InvalidOperationException>(() => Evaluate(expression));
    }

    [Fact]
    public void ConditionalWithUnknownConditionIsUnknown()
    {
        var result = Evaluate("u ? 1 : 2", ("u", Unknown));

        Assert.Equal(HclValueType.Unknown, result.Type);
    }
}
