using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests collection expression tokenization: tuples and objects.
/// </summary>
public sealed class Utf8HclReaderExpressionCollectionTests
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
    public void EmptyTuple()
    {
        var expr = ExprTokens("x = []\n");
        Assert.Equal(HclTokenType.OpenBracket, expr[0].Type);
        Assert.Equal(HclTokenType.CloseBracket, expr[1].Type);
    }

    [Fact]
    public void TupleWithElements()
    {
        var expr = ExprTokens("x = [1, 2, 3]\n");
        Assert.Equal(HclTokenType.OpenBracket, expr[0].Type);
        Assert.Equal(HclTokenType.NumberLiteral, expr[1].Type);
        Assert.Equal(HclTokenType.Comma, expr[2].Type);
        Assert.Equal(HclTokenType.NumberLiteral, expr[3].Type);
        Assert.Equal(HclTokenType.Comma, expr[4].Type);
        Assert.Equal(HclTokenType.NumberLiteral, expr[5].Type);
        Assert.Equal(HclTokenType.CloseBracket, expr[6].Type);
    }

    [Fact]
    public void TupleWithMixedTypes()
    {
        var expr = ExprTokens("x = [1, \"two\", true, null]\n");
        Assert.Equal(HclTokenType.NumberLiteral, expr[1].Type);
        Assert.Equal(HclTokenType.StringLiteral, expr[3].Type);
        Assert.Equal(HclTokenType.BoolLiteral, expr[5].Type);
        Assert.Equal(HclTokenType.NullLiteral, expr[7].Type);
    }

    [Fact]
    public void TupleWithTrailingComma()
    {
        var expr = ExprTokens("x = [1, 2,]\n");
        Assert.Equal(HclTokenType.OpenBracket, expr[0].Type);
        Assert.Equal(HclTokenType.CloseBracket, expr[^1].Type);
        Assert.Equal(2, expr.Count(t => t.Type == HclTokenType.Comma));
    }

    [Fact]
    public void TupleWithNewlineSeparators()
    {
        var expr = ExprTokens("x = [\n  1\n  2\n  3\n]\n");
        Assert.Equal(HclTokenType.OpenBracket, expr[0].Type);
        Assert.Equal(3, expr.Count(t => t.Type == HclTokenType.NumberLiteral));
        Assert.Equal(HclTokenType.CloseBracket, expr[^1].Type);
    }

    [Fact]
    public void EmptyObject()
    {
        var expr = ExprTokens("x = {}\n");
        Assert.Equal(HclTokenType.OpenBrace, expr[0].Type);
        Assert.Equal(HclTokenType.CloseBrace, expr[1].Type);
    }

    [Fact]
    public void ObjectWithEqualsSeperator()
    {
        var expr = ExprTokens("x = { key = \"value\" }\n");
        Assert.Equal(HclTokenType.OpenBrace, expr[0].Type);
        Assert.Contains(expr, t => t.Type == HclTokenType.Identifier && t.Value == "key");
        Assert.Contains(expr, t => t.Type == HclTokenType.Equals);
        Assert.Contains(expr, t => t.Type == HclTokenType.StringLiteral && t.Value == "value");
        Assert.Equal(HclTokenType.CloseBrace, expr[^1].Type);
    }

    [Fact]
    public void ObjectWithColonSeparator()
    {
        var expr = ExprTokens("x = { key: \"value\" }\n");
        Assert.Contains(expr, t => t.Type == HclTokenType.Colon);
    }

    [Fact]
    public void ObjectWithStringKeys()
    {
        var expr = ExprTokens("x = { \"foo\" = 1, \"bar\" = 2 }\n");
        int stringCount = expr.Count(t => t.Type == HclTokenType.StringLiteral);
        Assert.True(stringCount >= 2); // at least "foo" and "bar"
    }

    [Fact]
    public void ObjectWithExpressionKeys()
    {
        var expr = ExprTokens("x = { (var.key) = \"value\" }\n");
        Assert.Equal(HclTokenType.OpenBrace, expr[0].Type);
        Assert.Equal(HclTokenType.OpenParen, expr[1].Type);
        Assert.Contains(expr, t => t.Value == "var");
        Assert.Equal(HclTokenType.CloseBrace, expr[^1].Type);
    }

    [Fact]
    public void ObjectWithTrailingComma()
    {
        var expr = ExprTokens("x = { a = 1, }\n");
        Assert.Contains(expr, t => t.Type == HclTokenType.Comma);
    }

    [Fact]
    public void NestedCollections()
    {
        var expr = ExprTokens("x = [{ a = [1, 2] }]\n");
        Assert.Equal(HclTokenType.OpenBracket, expr[0].Type);
        Assert.Equal(HclTokenType.OpenBrace, expr[1].Type);
        // Inner bracket for nested list
        Assert.True(expr.Count(t => t.Type == HclTokenType.OpenBracket) == 2);
    }

    [Fact]
    public void ObjectWithNewlineSeparators()
    {
        var expr = ExprTokens("x = {\n  a = 1\n  b = 2\n}\n");
        Assert.Equal(HclTokenType.OpenBrace, expr[0].Type);
        Assert.Contains(expr, t => t.Value == "a");
        Assert.Contains(expr, t => t.Value == "b");
        Assert.Equal(HclTokenType.CloseBrace, expr[^1].Type);
    }

    [Fact]
    public void TupleOfStrings()
    {
        var expr = ExprTokens("x = [\"a\", \"b\", \"c\"]\n");
        Assert.Equal(3, expr.Count(t => t.Type == HclTokenType.StringLiteral));
    }

    [Fact]
    public void DeeplyNestedCollections()
    {
        var expr = ExprTokens("x = [[1, [2, [3]]]]\n");
        Assert.Equal(4, expr.Count(t => t.Type == HclTokenType.OpenBracket));
        Assert.Equal(4, expr.Count(t => t.Type == HclTokenType.CloseBracket));
    }

    [Fact]
    public void ObjectMultipleKeyValuePairs()
    {
        var expr = ExprTokens("x = { foo = 1, bar = 2, baz = 3 }\n");
        Assert.Equal(3, expr.Count(t => t.Type == HclTokenType.Equals));
        Assert.Equal(2, expr.Count(t => t.Type == HclTokenType.Comma));
    }
}
