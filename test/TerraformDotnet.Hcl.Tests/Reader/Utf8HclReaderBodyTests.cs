using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderBodyTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    [Fact]
    public void EmptyBody()
    {
        var reader = CreateReader("");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Eof, reader.TokenType);
        Assert.False(reader.Read());
    }

    [Fact]
    public void BodyWithOnlyAttributes()
    {
        string hcl = "a = 1\nb = \"two\"\nc = true\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("a", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // 1

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("b", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // "two"

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("c", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // true
    }

    [Fact]
    public void BodyWithOnlyBlocks()
    {
        string hcl = "a {}\nb {}\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // BlockEnd

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
    }

    [Fact]
    public void BodyWithMixedAttributesAndBlocks()
    {
        string hcl = "x = 1\nblock {\n  y = 2\n}\nz = 3\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // AttributeName "x"
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); // 1

        Assert.True(reader.Read()); // BlockType "block"
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // AttributeName "y"
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); // 2
        Assert.True(reader.Read()); // BlockEnd

        Assert.True(reader.Read()); // AttributeName "z"
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); // 3
    }

    [Fact]
    public void BodyWithCommentsInterspersed()
    {
        string hcl = "# header\na = 1\n# between\nb = 2\n";
        var reader = CreateReader(hcl);

        // Comments are skipped by default
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("a", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // 1

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("b", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BodyWithBlankLinesBetweenElements()
    {
        string hcl = "a = 1\n\n\nb = 2\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("a", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // 1

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("b", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void FileWithMultipleTopLevelBlocks_TypicalTerraform()
    {
        string hcl = """
            provider "aws" {
              region = "us-east-1"
            }

            resource "aws_instance" "web" {
              ami           = "ami-123"
              instance_type = "t2.micro"
            }
            """;
        var reader = CreateReader(hcl);

        // Provider block
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("provider", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read()); // BlockLabel "aws"
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // AttributeName "region"
        Assert.True(reader.Read()); // StringLiteral
        Assert.True(reader.Read()); // BlockEnd

        // Resource block
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("resource", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BodyWithOnlyWhitespaceAndNewlines()
    {
        var reader = CreateReader("   \n\n   \n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Eof, reader.TokenType);
    }

    [Fact]
    public void BodyWithOnlyCommentsAndWhitespace()
    {
        var reader = CreateReader("# comment\n\n# another\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Eof, reader.TokenType);
    }

    [Fact]
    public void BodyTrailingWhitespace()
    {
        var reader = CreateReader("x = 1\n   \n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // NumberLiteral
        Assert.True(reader.Read()); // Eof
        Assert.Equal(HclTokenType.Eof, reader.TokenType);
    }

    [Fact]
    public void BytesConsumed_TracksPosition()
    {
        var reader = CreateReader("x = 1\n");
        Assert.Equal(0, reader.BytesConsumed);
        Assert.True(reader.Read()); // AttributeName 'x'
        // After reading "x" and skipping "= ", position should be past the '='
        Assert.True(reader.BytesConsumed > 0);
    }

    [Fact]
    public void Position_TracksLineAndColumn()
    {
        var reader = CreateReader("x = 1\ny = 2\n");
        Assert.True(reader.Read()); // AttributeName 'x'
        Mark start = reader.TokenStart;
        Assert.Equal(1, start.Line);
        Assert.Equal(1, start.Column);

        Assert.True(reader.Read()); // NumberLiteral 1
        Assert.True(reader.Read()); // AttributeName 'y'
        Mark yStart = reader.TokenStart;
        Assert.Equal(2, yStart.Line);
    }
}
