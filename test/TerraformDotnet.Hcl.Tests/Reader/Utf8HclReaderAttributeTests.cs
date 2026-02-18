using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderAttributeTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    [Fact]
    public void SimpleStringAttribute()
    {
        var reader = CreateReader("name = \"value\"\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("name", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        Assert.Equal("value", reader.GetString());
    }

    [Fact]
    public void NumberAttribute()
    {
        var reader = CreateReader("count = 42\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("count", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal(42, reader.GetInt32());
    }

    [Fact]
    public void BoolAttribute_True()
    {
        var reader = CreateReader("enabled = true\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BoolLiteral, reader.TokenType);
        Assert.True(reader.GetBoolean());
    }

    [Fact]
    public void BoolAttribute_False()
    {
        var reader = CreateReader("enabled = false\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BoolLiteral, reader.TokenType);
        Assert.False(reader.GetBoolean());
    }

    [Fact]
    public void NullAttribute()
    {
        var reader = CreateReader("value = null\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NullLiteral, reader.TokenType);
        Assert.Null(reader.GetString());
    }

    [Fact]
    public void ExpressionAttribute()
    {
        var reader = CreateReader("total = var.a + var.b\n");
        Assert.True(reader.Read()); // AttributeName "total"
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);

        // The expression tokens follow
        Assert.True(reader.Read()); // Identifier "var"
        Assert.Equal(HclTokenType.Identifier, reader.TokenType);
        Assert.True(reader.Read()); // Dot
        Assert.Equal(HclTokenType.Dot, reader.TokenType);
        Assert.True(reader.Read()); // Identifier "a"
        Assert.Equal(HclTokenType.Identifier, reader.TokenType);
        Assert.True(reader.Read()); // Plus
        Assert.Equal(HclTokenType.Plus, reader.TokenType);
    }

    [Fact]
    public void FunctionCallAttribute()
    {
        var reader = CreateReader("cidr = cidrsubnet(\"10.0.0.0/8\", 8, 1)\n");
        Assert.True(reader.Read()); // AttributeName "cidr"
        Assert.True(reader.Read()); // FunctionCall "cidrsubnet"
        Assert.Equal(HclTokenType.FunctionCall, reader.TokenType);
        Assert.Equal("cidrsubnet", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void ListAttribute()
    {
        var reader = CreateReader("tags = [\"a\", \"b\", \"c\"]\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // OpenBracket
        Assert.Equal(HclTokenType.OpenBracket, reader.TokenType);
        Assert.True(reader.Read()); // StringLiteral "a"
        Assert.Equal("a", reader.GetString());
        Assert.True(reader.Read()); // Comma
        Assert.True(reader.Read()); // StringLiteral "b"
        Assert.True(reader.Read()); // Comma
        Assert.True(reader.Read()); // StringLiteral "c"
        Assert.True(reader.Read()); // CloseBracket
        Assert.Equal(HclTokenType.CloseBracket, reader.TokenType);
    }

    [Fact]
    public void MapAttribute()
    {
        var reader = CreateReader("settings = { key = \"value\" }\n");
        Assert.True(reader.Read()); // AttributeName "settings"
        Assert.True(reader.Read()); // OpenBrace
        Assert.Equal(HclTokenType.OpenBrace, reader.TokenType);
    }

    [Fact]
    public void AttributeWithConditional()
    {
        var reader = CreateReader("size = var.large ? \"big\" : \"small\"\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.Equal("size", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // Identifier "var"
        Assert.True(reader.Read()); // Dot
        Assert.True(reader.Read()); // Identifier "large"
        Assert.True(reader.Read()); // Question
        Assert.Equal(HclTokenType.Question, reader.TokenType);
    }

    [Fact]
    public void AttributeWithHeredoc()
    {
        string hcl = "description = <<EOF\nsome text\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read()); // AttributeName
        Assert.Equal("description", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // StringLiteral (heredoc)
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        Assert.Equal(StringLiteralStyle.Heredoc, reader.StringStyle);
    }

    [Fact]
    public void AttributeFollowedByEof()
    {
        var reader = CreateReader("x = 1");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // NumberLiteral
        Assert.True(reader.Read()); // Eof
        Assert.Equal(HclTokenType.Eof, reader.TokenType);
    }

    [Fact]
    public void WhitespaceVariations_Compact()
    {
        var reader = CreateReader("name=\"value\"\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
    }

    [Fact]
    public void WhitespaceVariations_ExtraSpaces()
    {
        var reader = CreateReader("name  =  \"value\"\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
    }

    [Fact]
    public void MultipleAttributesInBody()
    {
        string hcl = "a = 1\nb = 2\nc = 3\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // AttributeName "a"
        Assert.Equal("a", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // NumberLiteral 1

        Assert.True(reader.Read()); // AttributeName "b"
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("b", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // NumberLiteral 2

        Assert.True(reader.Read()); // AttributeName "c"
        Assert.Equal("c", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // NumberLiteral 3
    }
}
