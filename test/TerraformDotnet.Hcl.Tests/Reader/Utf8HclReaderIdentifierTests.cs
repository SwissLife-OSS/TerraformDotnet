using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderIdentifierTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    /// <summary>
    /// Helper: reads attribute name and returns the identifier string.
    /// Input should be in form "identifier = value".
    /// </summary>
    private static string ReadAttributeName(string hcl)
    {
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        return Encoding.UTF8.GetString(reader.ValueSpan);
    }

    [Fact]
    public void SimpleAsciiIdentifier()
    {
        Assert.Equal("my_var", ReadAttributeName("my_var = 1\n"));
    }

    [Fact]
    public void IdentifierWithDashes()
    {
        Assert.Equal("my-var", ReadAttributeName("my-var = 1\n"));
    }

    [Fact]
    public void IdentifierWithUnderscores()
    {
        Assert.Equal("my_var", ReadAttributeName("my_var = 1\n"));
    }

    [Fact]
    public void IdentifierStartingWithUnderscore()
    {
        Assert.Equal("_private", ReadAttributeName("_private = 1\n"));
    }

    [Fact]
    public void IdentifierWithDigits()
    {
        Assert.Equal("var1", ReadAttributeName("var1 = 1\n"));
    }

    [Fact]
    public void IdentifierMixedCase()
    {
        Assert.Equal("MyVar", ReadAttributeName("MyVar = 1\n"));
    }

    [Fact]
    public void SingleCharIdentifier()
    {
        Assert.Equal("x", ReadAttributeName("x = 1\n"));
    }

    [Fact]
    public void KeywordAsAttributeName_For()
    {
        // Keywords are context-dependent in HCL; they can be used as attribute names
        Assert.Equal("for", ReadAttributeName("for = 1\n"));
    }

    [Fact]
    public void KeywordAsAttributeName_True()
    {
        Assert.Equal("true", ReadAttributeName("true = 1\n"));
    }

    [Fact]
    public void KeywordAsAttributeName_Null()
    {
        Assert.Equal("null", ReadAttributeName("null = 1\n"));
    }

    [Fact]
    public void KeywordAsAttributeName_If()
    {
        Assert.Equal("if", ReadAttributeName("if = 1\n"));
    }

    [Fact]
    public void IdentifierAllUnderscores()
    {
        Assert.Equal("___", ReadAttributeName("___ = 1\n"));
    }

    [Fact]
    public void IdentifierConsecutiveDashes()
    {
        Assert.Equal("a--b", ReadAttributeName("a--b = 1\n"));
    }

    [Fact]
    public void IdentifierEndingWithDash()
    {
        Assert.Equal("my-var-", ReadAttributeName("my-var- = 1\n"));
    }

    [Fact]
    public void IdentifierFollowedByEquals_IsAttribute()
    {
        var reader = CreateReader("name = \"value\"\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
    }

    [Fact]
    public void IdentifierFollowedByBrace_IsBlock()
    {
        var reader = CreateReader("locals {\n}\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("locals", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void IdentifierFollowedByString_IsBlockWithLabel()
    {
        var reader = CreateReader("resource \"aws_instance\" {\n}\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("resource", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockLabel, reader.TokenType);
        Assert.Equal("aws_instance", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void UnicodeIdentifier()
    {
        // Unicode letters are valid identifier starts (UAX#31)
        Assert.Equal("café", ReadAttributeName("café = 1\n"));
    }

    [Fact]
    public void VeryLongIdentifier()
    {
        string longName = new('a', 1000);
        Assert.Equal(longName, ReadAttributeName($"{longName} = 1\n"));
    }

    [Fact]
    public void IdentifierV2Name()
    {
        Assert.Equal("v2_name", ReadAttributeName("v2_name = 1\n"));
    }
}
