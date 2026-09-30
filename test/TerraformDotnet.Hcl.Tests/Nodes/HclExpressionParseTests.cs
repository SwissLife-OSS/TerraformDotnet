using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Nodes;

public sealed class HclExpressionParseTests
{
    [Fact]
    public void ParsesFunctionCall()
    {
        var expression = HclExpression.Parse("contains([\"a\", \"b\"], var.x)");

        var call = Assert.IsType<HclFunctionCallExpression>(expression);
        Assert.Equal("contains", call.Name);
        Assert.Equal(2, call.Arguments.Count);
    }

    [Fact]
    public void ParsesMultiLineExpression()
    {
        var expression = HclExpression.Parse("[\n  1,\n  2,\n]");

        Assert.IsType<HclTupleExpression>(expression);
    }

    [Fact]
    public void ParsesLiteral()
    {
        var expression = HclExpression.Parse("42");

        var literal = Assert.IsType<HclLiteralExpression>(expression);
        Assert.Equal("42", literal.Value);
    }

    [Theory]
    [InlineData("1\ny = 2")]
    [InlineData("")]
    [InlineData("foo(")]
    public void RejectsInvalidExpressions(string text)
    {
        Assert.ThrowsAny<HclException>(() => HclExpression.Parse(text));
    }

    [Fact]
    public void TryParseReturnsFalseForInvalidInput()
    {
        Assert.False(HclExpression.TryParse("foo(", out var result));
        Assert.Null(result);
        Assert.False(HclExpression.TryParse(null, out _));
    }

    [Fact]
    public void TryParseReturnsExpression()
    {
        Assert.True(HclExpression.TryParse("var.a == 1", out var result));
        Assert.IsType<HclBinaryExpression>(result);
    }
}
