using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests for-expression tokenization (tuple and object variants).
/// </summary>
public sealed class Utf8HclReaderExpressionForTests
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
    public void TupleFor_Simple()
    {
        // [for v in list : v]
        var expr = ExprTokens("x = [for v in list : v]\n");
        Assert.Equal(HclTokenType.OpenBracket, expr[0].Type);
        Assert.Equal(HclTokenType.Identifier, expr[1].Type);
        Assert.Equal("for", expr[1].Value);
        Assert.Equal(HclTokenType.Identifier, expr[2].Type);
        Assert.Equal("v", expr[2].Value);
        Assert.Equal(HclTokenType.Identifier, expr[3].Type);
        Assert.Equal("in", expr[3].Value);
        Assert.Equal(HclTokenType.Identifier, expr[4].Type);
        Assert.Equal("list", expr[4].Value);
        Assert.Equal(HclTokenType.Colon, expr[5].Type);
        Assert.Equal(HclTokenType.Identifier, expr[6].Type);
        Assert.Equal("v", expr[6].Value);
        Assert.Equal(HclTokenType.CloseBracket, expr[7].Type);
    }

    [Fact]
    public void TupleFor_WithIndex()
    {
        // [for i, v in list : v]
        var expr = ExprTokens("x = [for i, v in list : v]\n");
        Assert.Equal("for", expr[1].Value);
        Assert.Equal("i", expr[2].Value);
        Assert.Equal(HclTokenType.Comma, expr[3].Type);
        Assert.Equal("v", expr[4].Value);
        Assert.Equal("in", expr[5].Value);
    }

    [Fact]
    public void TupleFor_WithCondition()
    {
        // [for v in list : v if v != ""]
        var expr = ExprTokens("x = [for v in list : v if v != \"\"]\n");
        Assert.Contains(expr, t => t.Value == "if");
        Assert.Contains(expr, t => t.Type == HclTokenType.NotEqual);
    }

    [Fact]
    public void ObjectFor_Simple()
    {
        // {for k, v in map : k => v}
        var expr = ExprTokens("x = {for k, v in map : k => v}\n");
        Assert.Equal(HclTokenType.OpenBrace, expr[0].Type);
        Assert.Equal("for", expr[1].Value);
        Assert.Contains(expr, t => t.Type == HclTokenType.FatArrow);
        Assert.Equal(HclTokenType.CloseBrace, expr[^1].Type);
    }

    [Fact]
    public void ObjectFor_WithGrouping()
    {
        // {for v in list : v.key => v...}
        var expr = ExprTokens("x = {for v in list : v.key => v...}\n");
        Assert.Contains(expr, t => t.Type == HclTokenType.FatArrow);
        Assert.Contains(expr, t => t.Type == HclTokenType.Ellipsis);
    }

    [Fact]
    public void ForOverComplexExpression()
    {
        // [for v in concat(a, b) : upper(v)]
        var expr = ExprTokens("x = [for v in concat(a, b) : upper(v)]\n");
        Assert.Contains(expr, t => t.Type == HclTokenType.FunctionCall && t.Value == "concat");
        Assert.Contains(expr, t => t.Type == HclTokenType.FunctionCall && t.Value == "upper");
    }

    [Fact]
    public void ForWithComplexCondition()
    {
        // [for i, v in list : v if i < 3 && v != null]
        var expr = ExprTokens("x = [for i, v in list : v if i < 3 && v != null]\n");
        Assert.Contains(expr, t => t.Value == "if");
        Assert.Contains(expr, t => t.Type == HclTokenType.LessThan);
        Assert.Contains(expr, t => t.Type == HclTokenType.And);
        Assert.Contains(expr, t => t.Type == HclTokenType.NotEqual);
    }

    [Fact]
    public void NestedForExpressions()
    {
        // [for a in list : [for b in a : b]]
        var expr = ExprTokens("x = [for a in list : [for b in a : b]]\n");
        int bracketOpen = expr.Count(t => t.Type == HclTokenType.OpenBracket);
        int bracketClose = expr.Count(t => t.Type == HclTokenType.CloseBracket);
        Assert.Equal(2, bracketOpen);
        Assert.Equal(2, bracketClose);
        Assert.Equal(2, expr.Count(t => t.Value == "for"));
    }

    [Fact]
    public void ForSingleVariable_ObjectFor()
    {
        // {for v in list : v => v}
        var expr = ExprTokens("x = {for v in list : v => v}\n");
        Assert.Equal(HclTokenType.OpenBrace, expr[0].Type);
        Assert.Equal("for", expr[1].Value);
        Assert.Contains(expr, t => t.Type == HclTokenType.FatArrow);
    }
}
