using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests function call expression tokenization.
/// </summary>
public sealed class Utf8HclReaderExpressionFunctionTests
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
    public void NoArguments()
    {
        var expr = ExprTokens("x = timestamp()\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("timestamp", expr[0].Value);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Equal(HclTokenType.CloseParen, expr[2].Type);
    }

    [Fact]
    public void SingleArgument()
    {
        var expr = ExprTokens("x = length(list)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("length", expr[0].Value);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Equal(HclTokenType.Identifier, expr[2].Type);
        Assert.Equal("list", expr[2].Value);
        Assert.Equal(HclTokenType.CloseParen, expr[3].Type);
    }

    [Fact]
    public void MultipleArguments()
    {
        var expr = ExprTokens("x = substr(\"hello\", 0, 3)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("substr", expr[0].Value);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Equal(HclTokenType.StringLiteral, expr[2].Type);
        Assert.Equal(HclTokenType.Comma, expr[3].Type);
        Assert.Equal(HclTokenType.NumberLiteral, expr[4].Type);
        Assert.Equal(HclTokenType.Comma, expr[5].Type);
        Assert.Equal(HclTokenType.NumberLiteral, expr[6].Type);
        Assert.Equal(HclTokenType.CloseParen, expr[7].Type);
    }

    [Fact]
    public void ArgumentExpansion()
    {
        var expr = ExprTokens("x = merge(map1, map2...)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("merge", expr[0].Value);
        Assert.Contains(expr, t => t.Type == HclTokenType.Ellipsis);
    }

    [Fact]
    public void NestedFunctionCalls()
    {
        var expr = ExprTokens("x = upper(trim(\" hello \"))\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("upper", expr[0].Value);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Equal(HclTokenType.FunctionCall, expr[2].Type);
        Assert.Equal("trim", expr[2].Value);
        Assert.Equal(HclTokenType.OpenParen, expr[3].Type);
        Assert.Equal(HclTokenType.StringLiteral, expr[4].Type);
        Assert.Equal(HclTokenType.CloseParen, expr[5].Type);
        Assert.Equal(HclTokenType.CloseParen, expr[6].Type);
    }

    [Fact]
    public void FunctionWithExpressionArguments()
    {
        var expr = ExprTokens("x = max(a + b, c * d)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("max", expr[0].Value);
        Assert.Contains(expr, t => t.Type == HclTokenType.Plus);
        Assert.Contains(expr, t => t.Type == HclTokenType.Star);
        Assert.Contains(expr, t => t.Type == HclTokenType.Comma);
    }

    [Fact]
    public void FunctionCallAsPartOfLargerExpression()
    {
        var expr = ExprTokens("x = func(a) + func(b)\n");
        int funcCount = expr.Count(t => t.Type == HclTokenType.FunctionCall);
        Assert.Equal(2, funcCount);
        Assert.Contains(expr, t => t.Type == HclTokenType.Plus);
    }

    [Fact]
    public void TrailingComma()
    {
        var expr = ExprTokens("x = list(\"a\", \"b\",)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        // Two commas (including trailing)
        int commaCount = expr.Count(t => t.Type == HclTokenType.Comma);
        Assert.Equal(2, commaCount);
    }

    [Fact]
    public void NewlinesBetweenArguments()
    {
        var expr = ExprTokens("x = func(\n  a,\n  b,\n  c\n)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal(3, expr.Count(t => t.Type == HclTokenType.Identifier));
    }

    [Fact]
    public void FunctionCallNoSpaceBeforeParen()
    {
        var expr = ExprTokens("x = func(a)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("func", expr[0].Value);
    }

    [Fact]
    public void FunctionCallWithDotAccessOnResult()
    {
        // func(a).name
        var expr = ExprTokens("x = func(a).name\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Equal(HclTokenType.Identifier, expr[2].Type);
        Assert.Equal(HclTokenType.CloseParen, expr[3].Type);
        Assert.Equal(HclTokenType.Dot, expr[4].Type);
        Assert.Equal(HclTokenType.Identifier, expr[5].Type);
        Assert.Equal("name", expr[5].Value);
    }
}
