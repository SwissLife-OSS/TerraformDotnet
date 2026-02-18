using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests splat expression tokenization (.* and [*] forms).
/// </summary>
public sealed class Utf8HclReaderExpressionSplatTests
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
    public void AttributeSplat()
    {
        // list.*.name
        var expr = ExprTokens("x = list.*.name\n");
        Assert.Equal(HclTokenType.Identifier, expr[0].Type);
        Assert.Equal("list", expr[0].Value);
        Assert.Equal(HclTokenType.Dot, expr[1].Type);
        Assert.Equal(HclTokenType.Star, expr[2].Type);
        Assert.Equal(HclTokenType.Dot, expr[3].Type);
        Assert.Equal(HclTokenType.Identifier, expr[4].Type);
        Assert.Equal("name", expr[4].Value);
    }

    [Fact]
    public void FullSplat()
    {
        // list[*].name
        var expr = ExprTokens("x = list[*].name\n");
        Assert.Equal("list", expr[0].Value);
        Assert.Equal(HclTokenType.OpenBracket, expr[1].Type);
        Assert.Equal(HclTokenType.Star, expr[2].Type);
        Assert.Equal(HclTokenType.CloseBracket, expr[3].Type);
        Assert.Equal(HclTokenType.Dot, expr[4].Type);
        Assert.Equal("name", expr[5].Value);
    }

    [Fact]
    public void ChainedAttributeSplat()
    {
        // list.*.foo.bar
        var expr = ExprTokens("x = list.*.foo.bar\n");
        Assert.Equal("list", expr[0].Value);
        Assert.Equal(HclTokenType.Dot, expr[1].Type);
        Assert.Equal(HclTokenType.Star, expr[2].Type);
        Assert.Equal(HclTokenType.Dot, expr[3].Type);
        Assert.Equal("foo", expr[4].Value);
        Assert.Equal(HclTokenType.Dot, expr[5].Type);
        Assert.Equal("bar", expr[6].Value);
    }

    [Fact]
    public void FullSplatWithIndex()
    {
        // list[*].foo[0]
        var expr = ExprTokens("x = list[*].foo[0]\n");
        Assert.Contains(expr, t => t.Type == HclTokenType.Star);
        Assert.Contains(expr, t => t.Value == "foo");
        Assert.Contains(expr, t => t.Value == "0" && t.Type == HclTokenType.NumberLiteral);
    }

    [Fact]
    public void FullSplatWithMixedTraversal()
    {
        // list[*].foo.bar[0].baz
        var expr = ExprTokens("x = list[*].foo.bar[0].baz\n");
        Assert.Contains(expr, t => t.Type == HclTokenType.Star);
        Assert.Contains(expr, t => t.Value == "foo");
        Assert.Contains(expr, t => t.Value == "bar");
        Assert.Contains(expr, t => t.Value == "baz");
    }

    [Fact]
    public void IdentitySplat()
    {
        // list[*] — valid splat with no following traversal
        var expr = ExprTokens("x = list[*]\n");
        Assert.Equal("list", expr[0].Value);
        Assert.Equal(HclTokenType.OpenBracket, expr[1].Type);
        Assert.Equal(HclTokenType.Star, expr[2].Type);
        Assert.Equal(HclTokenType.CloseBracket, expr[3].Type);
    }

    [Fact]
    public void SplatInComplexExpression()
    {
        // length(list[*].name)
        var expr = ExprTokens("x = length(list[*].name)\n");
        Assert.Equal(HclTokenType.FunctionCall, expr[0].Type);
        Assert.Equal("length", expr[0].Value);
        Assert.Contains(expr, t => t.Type == HclTokenType.Star);
    }
}
