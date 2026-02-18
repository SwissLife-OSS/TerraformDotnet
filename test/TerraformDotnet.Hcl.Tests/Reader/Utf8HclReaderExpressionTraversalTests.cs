using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests variable references, attribute access chains, and index expressions.
/// </summary>
public sealed class Utf8HclReaderExpressionTraversalTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    /// <summary>Reads all tokens until Eof and returns them as token/value pairs.</summary>
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

    [Fact]
    public void SimpleVariable()
    {
        var tokens = ReadAllTokens("x = myvar\n");
        Assert.Equal(HclTokenType.AttributeName, tokens[0].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal("myvar", tokens[1].Value);
    }

    [Fact]
    public void AttributeAccess()
    {
        var tokens = ReadAllTokens("x = var.name\n");
        Assert.Equal(HclTokenType.AttributeName, tokens[0].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal("var", tokens[1].Value);
        Assert.Equal(HclTokenType.Dot, tokens[2].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[3].Type);
        Assert.Equal("name", tokens[3].Value);
    }

    [Fact]
    public void ChainedAttributeAccess()
    {
        var tokens = ReadAllTokens("x = module.network.vpc.id\n");
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal("module", tokens[1].Value);
        Assert.Equal(HclTokenType.Dot, tokens[2].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[3].Type);
        Assert.Equal("network", tokens[3].Value);
        Assert.Equal(HclTokenType.Dot, tokens[4].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[5].Type);
        Assert.Equal("vpc", tokens[5].Value);
        Assert.Equal(HclTokenType.Dot, tokens[6].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[7].Type);
        Assert.Equal("id", tokens[7].Value);
    }

    [Fact]
    public void IndexAccess()
    {
        var tokens = ReadAllTokens("x = list[0]\n");
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal("list", tokens[1].Value);
        Assert.Equal(HclTokenType.OpenBracket, tokens[2].Type);
        Assert.Equal(HclTokenType.NumberLiteral, tokens[3].Type);
        Assert.Equal("0", tokens[3].Value);
        Assert.Equal(HclTokenType.CloseBracket, tokens[4].Type);
    }

    [Fact]
    public void IndexWithExpression()
    {
        var tokens = ReadAllTokens("x = list[var.index]\n");
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal(HclTokenType.OpenBracket, tokens[2].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[3].Type);
        Assert.Equal("var", tokens[3].Value);
        Assert.Equal(HclTokenType.Dot, tokens[4].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[5].Type);
        Assert.Equal("index", tokens[5].Value);
        Assert.Equal(HclTokenType.CloseBracket, tokens[6].Type);
    }

    [Fact]
    public void CombinedAccessAndIndex()
    {
        // x = module.network.subnets[0].id
        var tokens = ReadAllTokens("x = module.network.subnets[0].id\n");
        Assert.Equal("module", tokens[1].Value);
        Assert.Equal(HclTokenType.Dot, tokens[2].Type);
        Assert.Equal("network", tokens[3].Value);
        Assert.Equal(HclTokenType.Dot, tokens[4].Type);
        Assert.Equal("subnets", tokens[5].Value);
        Assert.Equal(HclTokenType.OpenBracket, tokens[6].Type);
        Assert.Equal("0", tokens[7].Value);
        Assert.Equal(HclTokenType.CloseBracket, tokens[8].Type);
        Assert.Equal(HclTokenType.Dot, tokens[9].Type);
        Assert.Equal("id", tokens[10].Value);
    }

    [Fact]
    public void LegacyIndex()
    {
        // x = list.0 — legacy numeric index via dot
        var tokens = ReadAllTokens("x = list.0\n");
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal("list", tokens[1].Value);
        Assert.Equal(HclTokenType.Dot, tokens[2].Type);
        Assert.Equal(HclTokenType.NumberLiteral, tokens[3].Type);
        Assert.Equal("0", tokens[3].Value);
    }

    [Fact]
    public void DeeplyChainedAccess()
    {
        var tokens = ReadAllTokens("x = a.b.c.d.e.f.g.h\n");
        // Should produce: AttributeName, Identifier, (Dot Identifier) × 7
        Assert.Equal(HclTokenType.AttributeName, tokens[0].Type);
        int dotCount = tokens.Count(t => t.Type == HclTokenType.Dot);
        Assert.Equal(7, dotCount);
    }

    [Fact]
    public void IndexWithStringKey()
    {
        var tokens = ReadAllTokens("x = map[\"key\"]\n");
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal("map", tokens[1].Value);
        Assert.Equal(HclTokenType.OpenBracket, tokens[2].Type);
        Assert.Equal(HclTokenType.StringLiteral, tokens[3].Type);
        Assert.Equal("key", tokens[3].Value);
        Assert.Equal(HclTokenType.CloseBracket, tokens[4].Type);
    }

    [Fact]
    public void NewlinesInsideIndexBrackets()
    {
        // Newlines inside brackets are allowed
        var tokens = ReadAllTokens("x = list[\n  0\n]\n");
        Assert.Equal(HclTokenType.Identifier, tokens[1].Type);
        Assert.Equal(HclTokenType.OpenBracket, tokens[2].Type);
        Assert.Equal(HclTokenType.NumberLiteral, tokens[3].Type);
        Assert.Equal(HclTokenType.CloseBracket, tokens[4].Type);
    }

    [Fact]
    public void TraversalFollowedByAnotherAttribute()
    {
        var tokens = ReadAllTokens("x = var.name\ny = var.other\n");
        Assert.Equal(HclTokenType.AttributeName, tokens[0].Type);
        Assert.Equal("x", tokens[0].Value);
        Assert.Equal("var", tokens[1].Value);
        Assert.Equal(HclTokenType.Dot, tokens[2].Type);
        Assert.Equal("name", tokens[3].Value);
        // Second attribute
        Assert.Equal(HclTokenType.AttributeName, tokens[4].Type);
        Assert.Equal("y", tokens[4].Value);
    }

    [Fact]
    public void AttributeAccessOnFunctionResult()
    {
        // func(a).name — function call followed by dot access
        var tokens = ReadAllTokens("x = func(a).name\n");
        Assert.Equal(HclTokenType.FunctionCall, tokens[1].Type);
        Assert.Equal("func", tokens[1].Value);
        Assert.Equal(HclTokenType.OpenParen, tokens[2].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[3].Type);
        Assert.Equal(HclTokenType.CloseParen, tokens[4].Type);
        Assert.Equal(HclTokenType.Dot, tokens[5].Type);
        Assert.Equal(HclTokenType.Identifier, tokens[6].Type);
        Assert.Equal("name", tokens[6].Value);
    }
}
