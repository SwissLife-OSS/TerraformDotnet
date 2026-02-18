using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests conditional (ternary) expression tokenization.
/// </summary>
public sealed class Utf8HclReaderExpressionConditionalTests
{
    private static List<(HclTokenType Type, string Value)> ExprTokens(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        var reader = new Utf8HclReader(bytes);
        var tokens = new List<(HclTokenType, string)>();
        while (reader.Read())
        {
            string value = reader.ValueSpan.Length > 0
                ? Encoding.UTF8.GetString(reader.ValueSpan)
                : "";
            if (reader.TokenType != HclTokenType.AttributeName && reader.TokenType != HclTokenType.Eof)
            {
                tokens.Add((reader.TokenType, value));
            }
        }

        return tokens;
    }

    [Fact]
    public void SimpleConditional()
    {
        // x = cond ? "yes" : "no"
        var expr = ExprTokens("x = cond ? \"yes\" : \"no\"\n");
        Assert.Equal(HclTokenType.Identifier, expr[0].Type);
        Assert.Equal("cond", expr[0].Value);
        Assert.Equal(HclTokenType.Question, expr[1].Type);
        Assert.Equal(HclTokenType.StringLiteral, expr[2].Type);
        Assert.Equal("yes", expr[2].Value);
        Assert.Equal(HclTokenType.Colon, expr[3].Type);
        Assert.Equal(HclTokenType.StringLiteral, expr[4].Type);
        Assert.Equal("no", expr[4].Value);
    }

    [Fact]
    public void ConditionalWithVariablePredicate()
    {
        var expr = ExprTokens("x = var.enabled ? 1 : 0\n");
        Assert.Equal(HclTokenType.Identifier, expr[0].Type);
        Assert.Equal("var", expr[0].Value);
        Assert.Equal(HclTokenType.Dot, expr[1].Type);
        Assert.Equal("enabled", expr[2].Value);
        Assert.Equal(HclTokenType.Question, expr[3].Type);
        Assert.Equal(HclTokenType.NumberLiteral, expr[4].Type);
        Assert.Equal(HclTokenType.Colon, expr[5].Type);
        Assert.Equal(HclTokenType.NumberLiteral, expr[6].Type);
    }

    [Fact]
    public void ConditionalWithComparisonPredicate()
    {
        var expr = ExprTokens("x = a > 0 ? a : 0\n");
        Assert.Equal("a", expr[0].Value);
        Assert.Equal(HclTokenType.GreaterThan, expr[1].Type);
        Assert.Equal("0", expr[2].Value);
        Assert.Equal(HclTokenType.Question, expr[3].Type);
        Assert.Equal("a", expr[4].Value);
        Assert.Equal(HclTokenType.Colon, expr[5].Type);
        Assert.Equal("0", expr[6].Value);
    }

    [Fact]
    public void ConditionalWithStringBranches()
    {
        var expr = ExprTokens("x = var.large ? \"big\" : \"small\"\n");
        Assert.Equal(HclTokenType.Question, expr[3].Type);
        Assert.Equal("big", expr[4].Value);
        Assert.Equal(HclTokenType.Colon, expr[5].Type);
        Assert.Equal("small", expr[6].Value);
    }

    [Fact]
    public void ConditionalUsedAsFunctionArgument()
    {
        // func(a ? b : c)
        var expr = ExprTokens("x = func(a ? b : c)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("func", expr[0].Value);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Equal("a", expr[2].Value);
        Assert.Equal(HclTokenType.Question, expr[3].Type);
        Assert.Equal("b", expr[4].Value);
        Assert.Equal(HclTokenType.Colon, expr[5].Type);
        Assert.Equal("c", expr[6].Value);
        Assert.Equal(HclTokenType.CloseParen, expr[7].Type);
    }

    [Fact]
    public void ConditionalWithOperatorsOnBothSides()
    {
        var expr = ExprTokens("x = a > 0 ? a * 2 : a * -1\n");
        Assert.Contains(expr, t => t.Type == HclTokenType.Question);
        Assert.Contains(expr, t => t.Type == HclTokenType.Colon);
        Assert.Contains(expr, t => t.Type == HclTokenType.Star);
    }

    [Fact]
    public void ConditionalWithBoolLiteral()
    {
        var expr = ExprTokens("x = true ? \"a\" : \"b\"\n");
        Assert.Equal(HclTokenType.BoolLiteral, expr[0].Type);
        Assert.Equal(HclTokenType.Question, expr[1].Type);
    }
}
