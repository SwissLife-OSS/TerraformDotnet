using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderStringTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    /// <summary>
    /// Reads "x = {stringLiteral}" and returns the decoded string value.
    /// </summary>
    private static string? ReadStringValue(string hcl)
    {
        var reader = CreateReader(hcl);
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // StringLiteral
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        return reader.GetString();
    }

    [Fact]
    public void SimpleString()
    {
        Assert.Equal("hello", ReadStringValue("x = \"hello\"\n"));
    }

    [Fact]
    public void EmptyString()
    {
        Assert.Equal("", ReadStringValue("x = \"\"\n"));
    }

    [Fact]
    public void StringWithNewlineEscape()
    {
        Assert.Equal("line1\nline2", ReadStringValue("x = \"line1\\nline2\"\n"));
    }

    [Fact]
    public void StringWithTabEscape()
    {
        Assert.Equal("tab\there", ReadStringValue("x = \"tab\\there\"\n"));
    }

    [Fact]
    public void StringWithQuoteEscape()
    {
        Assert.Equal("quote\"inside", ReadStringValue("x = \"quote\\\"inside\"\n"));
    }

    [Fact]
    public void StringWithBackslashEscape()
    {
        Assert.Equal("path\\to\\file", ReadStringValue("x = \"path\\\\to\\\\file\"\n"));
    }

    [Fact]
    public void UnicodeEscape4Digit()
    {
        Assert.Equal("A", ReadStringValue("x = \"\\u0041\"\n"));
    }

    [Fact]
    public void UnicodeEscape8Digit()
    {
        Assert.Equal("A", ReadStringValue("x = \"\\U00000041\"\n"));
    }

    [Fact]
    public void StringWithInterpolation_RawValueContainsDollarBrace()
    {
        var reader = CreateReader("x = \"Hello, ${var.name}!\"\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // StringLiteral
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        string raw = Encoding.UTF8.GetString(reader.ValueSpan);
        Assert.Contains("${var.name}", raw);
    }

    [Fact]
    public void StringAsBlockLabel()
    {
        var reader = CreateReader("resource \"aws_instance\" {\n}\n");
        Assert.True(reader.Read()); // BlockType
        Assert.True(reader.Read()); // BlockLabel
        Assert.Equal(HclTokenType.BlockLabel, reader.TokenType);
        Assert.Equal("aws_instance", reader.GetString());
    }

    [Fact]
    public void MultiWordString()
    {
        Assert.Equal("hello world", ReadStringValue("x = \"hello world\"\n"));
    }

    [Fact]
    public void StringWithSpecialChars()
    {
        // Non-escape special characters are literal
        Assert.Equal("@#$%^&*", ReadStringValue("x = \"@#$%^&*\"\n"));
    }

    [Fact]
    public void UnterminatedString_Throws()
    {
        var ex = Record.Exception(() =>
        {
            byte[] b = Encoding.UTF8.GetBytes("x = \"no closing quote\n");
            var r = new Utf8HclReader(b);
            while (r.Read()) { }
        });
        Assert.IsType<HclSyntaxException>(ex);
    }

    [Fact]
    public void StringWithLiteralNewline_Throws()
    {
        // A raw newline inside a quoted string is an error
        var ex = Record.Exception(() =>
        {
            byte[] b = Encoding.UTF8.GetBytes("x = \"line1\nline2\"\n");
            var r = new Utf8HclReader(b);
            while (r.Read()) { }
        });
        Assert.IsType<HclSyntaxException>(ex);
    }

    [Fact]
    public void StringWithOnlyEscapes()
    {
        Assert.Equal("\n\t\r", ReadStringValue("x = \"\\n\\t\\r\"\n"));
    }

    [Fact]
    public void StringStyle_IsQuoted()
    {
        var reader = CreateReader("x = \"hello\"\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // StringLiteral
        Assert.Equal(StringLiteralStyle.Quoted, reader.StringStyle);
    }

    [Fact]
    public void VeryLongString()
    {
        string longContent = new('a', 10000);
        string hcl = $"x = \"{longContent}\"\n";
        Assert.Equal(longContent, ReadStringValue(hcl));
    }

    [Fact]
    public void StringWithCarriageReturn()
    {
        Assert.Equal("a\rb", ReadStringValue("x = \"a\\rb\"\n"));
    }
}
