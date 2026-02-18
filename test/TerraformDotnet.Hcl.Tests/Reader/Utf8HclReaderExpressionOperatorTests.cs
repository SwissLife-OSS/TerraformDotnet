using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests arithmetic, comparison, and logic operator tokenization in expressions,
/// including multi-token expressions and parenthesized grouping.
/// </summary>
public sealed class Utf8HclReaderExpressionOperatorTests
{
    private static List<(HclTokenType Type, string Value)> ReadAllTokens(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        var reader = new Utf8HclReader(bytes);
        var tokens = new List<(HclTokenType, string)>();
        while (reader.Read())
        {
            string value = reader.ValueSpan.Length > 0
                ? Encoding.UTF8.GetString(reader.ValueSpan)
                : "";
            tokens.Add((reader.TokenType, value));
        }

        return tokens;
    }

    /// <summary>Returns only the expression tokens (skips the AttributeName).</summary>
    private static List<(HclTokenType Type, string Value)> ExprTokens(string exprHcl)
    {
        var all = ReadAllTokens(exprHcl);
        return all.Where(t => t.Type != HclTokenType.AttributeName && t.Type != HclTokenType.Eof).ToList();
    }

    [Fact]
    public void Addition()
    {
        var expr = ExprTokens("x = a + b\n");
        Assert.Equal(HclTokenType.Identifier, expr[0].Type);
        Assert.Equal(HclTokenType.Plus, expr[1].Type);
        Assert.Equal(HclTokenType.Identifier, expr[2].Type);
    }

    [Fact]
    public void Subtraction()
    {
        var expr = ExprTokens("x = a - b\n");
        Assert.Equal(HclTokenType.Identifier, expr[0].Type);
        Assert.Equal(HclTokenType.Minus, expr[1].Type);
        Assert.Equal(HclTokenType.Identifier, expr[2].Type);
    }

    [Fact]
    public void Multiplication()
    {
        var expr = ExprTokens("x = a * b\n");
        Assert.Equal(HclTokenType.Star, expr[1].Type);
    }

    [Fact]
    public void Division()
    {
        var expr = ExprTokens("x = a / b\n");
        Assert.Equal(HclTokenType.Slash, expr[1].Type);
    }

    [Fact]
    public void Modulo()
    {
        var expr = ExprTokens("x = a % b\n");
        Assert.Equal(HclTokenType.Percent, expr[1].Type);
    }

    [Fact]
    public void UnaryNegation()
    {
        var expr = ExprTokens("x = -a\n");
        Assert.Equal(HclTokenType.Minus, expr[0].Type);
        Assert.Equal(HclTokenType.Identifier, expr[1].Type);
    }

    [Fact]
    public void Equality()
    {
        var expr = ExprTokens("x = a == b\n");
        Assert.Equal(HclTokenType.EqualEqual, expr[1].Type);
    }

    [Fact]
    public void NotEqual()
    {
        var expr = ExprTokens("x = a != b\n");
        Assert.Equal(HclTokenType.NotEqual, expr[1].Type);
    }

    [Fact]
    public void LessThan()
    {
        var expr = ExprTokens("x = a < b\n");
        Assert.Equal(HclTokenType.LessThan, expr[1].Type);
    }

    [Fact]
    public void GreaterThan()
    {
        var expr = ExprTokens("x = a > b\n");
        Assert.Equal(HclTokenType.GreaterThan, expr[1].Type);
    }

    [Fact]
    public void LessEqual()
    {
        var expr = ExprTokens("x = a <= b\n");
        Assert.Equal(HclTokenType.LessEqual, expr[1].Type);
    }

    [Fact]
    public void GreaterEqual()
    {
        var expr = ExprTokens("x = a >= b\n");
        Assert.Equal(HclTokenType.GreaterEqual, expr[1].Type);
    }

    [Fact]
    public void LogicalAnd()
    {
        var expr = ExprTokens("x = a && b\n");
        Assert.Equal(HclTokenType.And, expr[1].Type);
    }

    [Fact]
    public void LogicalOr()
    {
        var expr = ExprTokens("x = a || b\n");
        Assert.Equal(HclTokenType.Or, expr[1].Type);
    }

    [Fact]
    public void LogicalNot()
    {
        var expr = ExprTokens("x = !a\n");
        Assert.Equal(HclTokenType.Not, expr[0].Type);
        Assert.Equal(HclTokenType.Identifier, expr[1].Type);
    }

    [Fact]
    public void ComplexExpression_MixedOperators()
    {
        // x = a + b * c
        var expr = ExprTokens("x = a + b * c\n");
        Assert.Equal(5, expr.Count);
        Assert.Equal("a", expr[0].Value);
        Assert.Equal(HclTokenType.Plus, expr[1].Type);
        Assert.Equal("b", expr[2].Value);
        Assert.Equal(HclTokenType.Star, expr[3].Type);
        Assert.Equal("c", expr[4].Value);
    }

    [Fact]
    public void ParenthesizedExpression()
    {
        var expr = ExprTokens("x = (a + b) * c\n");
        Assert.Equal(HclTokenType.OpenParen, expr[0].Type);
        Assert.Equal("a", expr[1].Value);
        Assert.Equal(HclTokenType.Plus, expr[2].Type);
        Assert.Equal("b", expr[3].Value);
        Assert.Equal(HclTokenType.CloseParen, expr[4].Type);
        Assert.Equal(HclTokenType.Star, expr[5].Type);
        Assert.Equal("c", expr[6].Value);
    }

    [Fact]
    public void NestedParens()
    {
        // Tokens: OpenParen, OpenParen, Identifier "a", Plus, Identifier "b", CloseParen, CloseParen, Star, Identifier "c"
        var expr = ExprTokens("x = ((a + b)) * c\n");
        Assert.Equal(HclTokenType.OpenParen, expr[0].Type);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Equal(HclTokenType.CloseParen, expr[5].Type);
        Assert.Equal(HclTokenType.CloseParen, expr[6].Type);
    }

    [Fact]
    public void UnaryMinusOnExpression()
    {
        var expr = ExprTokens("x = -(a + b)\n");
        Assert.Equal(HclTokenType.Minus, expr[0].Type);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
    }

    [Fact]
    public void DoubleNegation()
    {
        var expr = ExprTokens("x = !!a\n");
        Assert.Equal(HclTokenType.Not, expr[0].Type);
        Assert.Equal(HclTokenType.Not, expr[1].Type);
        Assert.Equal(HclTokenType.Identifier, expr[2].Type);
    }

    [Fact]
    public void AllPrecedenceLevelsCombined()
    {
        // x = !a || b && c == d + e * f
        var expr = ExprTokens("x = !a || b && c == d + e * f\n");
        Assert.Equal(HclTokenType.Not, expr[0].Type);
        Assert.Equal("a", expr[1].Value);
        Assert.Equal(HclTokenType.Or, expr[2].Type);
        Assert.Equal("b", expr[3].Value);
        Assert.Equal(HclTokenType.And, expr[4].Type);
        Assert.Equal("c", expr[5].Value);
        Assert.Equal(HclTokenType.EqualEqual, expr[6].Type);
        Assert.Equal("d", expr[7].Value);
        Assert.Equal(HclTokenType.Plus, expr[8].Type);
        Assert.Equal("e", expr[9].Value);
        Assert.Equal(HclTokenType.Star, expr[10].Type);
        Assert.Equal("f", expr[11].Value);
    }

    [Fact]
    public void NewlinesInsideParenthesizedExpression()
    {
        // Newlines inside parens are allowed
        var expr = ExprTokens("x = (\n  a\n  +\n  b\n)\n");
        Assert.Equal(HclTokenType.OpenParen, expr[0].Type);
        Assert.Equal("a", expr[1].Value);
        Assert.Equal(HclTokenType.Plus, expr[2].Type);
        Assert.Equal("b", expr[3].Value);
        Assert.Equal(HclTokenType.CloseParen, expr[4].Type);
    }

    [Fact]
    public void ComparisonWithArithmetic()
    {
        var expr = ExprTokens("x = a + b > c && d\n");
        Assert.Equal("a", expr[0].Value);
        Assert.Equal(HclTokenType.Plus, expr[1].Type);
        Assert.Equal("b", expr[2].Value);
        Assert.Equal(HclTokenType.GreaterThan, expr[3].Type);
        Assert.Equal("c", expr[4].Value);
        Assert.Equal(HclTokenType.And, expr[5].Type);
        Assert.Equal("d", expr[6].Value);
    }
}
