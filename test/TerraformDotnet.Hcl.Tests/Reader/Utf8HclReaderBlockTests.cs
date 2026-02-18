using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderBlockTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    [Fact]
    public void BlockNoLabels()
    {
        var reader = CreateReader("locals {\n}\n");

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("locals", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockStart, reader.TokenType);
        Assert.Equal(1, reader.CurrentDepth);

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockEnd, reader.TokenType);
        Assert.Equal(0, reader.CurrentDepth);
    }

    [Fact]
    public void BlockOneQuotedLabel()
    {
        var reader = CreateReader("resource \"aws_instance\" {\n}\n");

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("resource", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockLabel, reader.TokenType);
        Assert.Equal("aws_instance", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockStart, reader.TokenType);
    }

    [Fact]
    public void BlockTwoQuotedLabels()
    {
        var reader = CreateReader("resource \"aws_instance\" \"web\" {\n}\n");

        Assert.True(reader.Read()); // BlockType
        Assert.Equal("resource", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read()); // BlockLabel 1
        Assert.Equal(HclTokenType.BlockLabel, reader.TokenType);
        Assert.Equal("aws_instance", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read()); // BlockLabel 2
        Assert.Equal(HclTokenType.BlockLabel, reader.TokenType);
        Assert.Equal("web", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read()); // BlockStart
        Assert.Equal(HclTokenType.BlockStart, reader.TokenType);
    }

    [Fact]
    public void BlockWithIdentifierLabel()
    {
        var reader = CreateReader("variable my_var {\n}\n");

        Assert.True(reader.Read()); // BlockType
        Assert.True(reader.Read()); // BlockLabel (identifier)
        Assert.Equal(HclTokenType.BlockLabel, reader.TokenType);
        Assert.Equal("my_var", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // BlockStart
    }

    [Fact]
    public void EmptyBlock()
    {
        var reader = CreateReader("resource \"null\" \"empty\" {}\n");

        Assert.True(reader.Read()); // BlockType
        Assert.True(reader.Read()); // BlockLabel 1
        Assert.True(reader.Read()); // BlockLabel 2
        Assert.True(reader.Read()); // BlockStart
        Assert.Equal(HclTokenType.BlockStart, reader.TokenType);

        Assert.True(reader.Read()); // BlockEnd
        Assert.Equal(HclTokenType.BlockEnd, reader.TokenType);
    }

    [Fact]
    public void NestedBlocks()
    {
        string hcl = """
            resource "aws_instance" "web" {
              provisioner "local-exec" {
                command = "echo hello"
              }
            }
            """;
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // BlockType "resource"
        Assert.True(reader.Read()); // BlockLabel "aws_instance"
        Assert.True(reader.Read()); // BlockLabel "web"
        Assert.True(reader.Read()); // BlockStart (depth 1)
        Assert.Equal(1, reader.CurrentDepth);

        Assert.True(reader.Read()); // BlockType "provisioner"
        Assert.Equal("provisioner", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // BlockLabel "local-exec"
        Assert.True(reader.Read()); // BlockStart (depth 2)
        Assert.Equal(2, reader.CurrentDepth);

        Assert.True(reader.Read()); // AttributeName "command"
        Assert.True(reader.Read()); // StringLiteral

        Assert.True(reader.Read()); // BlockEnd (depth 1)
        Assert.Equal(1, reader.CurrentDepth);

        Assert.True(reader.Read()); // BlockEnd (depth 0)
        Assert.Equal(0, reader.CurrentDepth);
    }

    [Fact]
    public void BlockContainingAttributesAndSubBlocks()
    {
        string hcl = """
            resource "aws_instance" "web" {
              ami = "ami-123"
              lifecycle {
                create_before_destroy = true
              }
              instance_type = "t2.micro"
            }
            """;
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // BlockType "resource"
        Assert.True(reader.Read()); // BlockLabel
        Assert.True(reader.Read()); // BlockLabel
        Assert.True(reader.Read()); // BlockStart

        Assert.True(reader.Read()); // AttributeName "ami"
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); // StringLiteral

        Assert.True(reader.Read()); // BlockType "lifecycle"
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // AttributeName "create_before_destroy"
        Assert.True(reader.Read()); // BoolLiteral

        Assert.True(reader.Read()); // BlockEnd (lifecycle)

        Assert.True(reader.Read()); // AttributeName "instance_type"
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); // StringLiteral

        Assert.True(reader.Read()); // BlockEnd (resource)
    }

    [Fact]
    public void MultipleTopLevelBlocks()
    {
        string hcl = "a {}\nb {}\nc {}\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // BlockType "a"
        Assert.Equal("a", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // BlockEnd

        Assert.True(reader.Read()); // BlockType "b"
        Assert.Equal("b", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // BlockEnd

        Assert.True(reader.Read()); // BlockType "c"
        Assert.Equal("c", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BlockWithThreeLabels()
    {
        var reader = CreateReader("block \"l1\" \"l2\" \"l3\" {\n}\n");
        Assert.True(reader.Read()); // BlockType
        Assert.True(reader.Read()); // BlockLabel 1
        Assert.Equal("l1", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // BlockLabel 2
        Assert.Equal("l2", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // BlockLabel 3
        Assert.Equal("l3", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read()); // BlockStart
        Assert.Equal(HclTokenType.BlockStart, reader.TokenType);
    }

    [Fact]
    public void DeeplyNestedBlocks_MaxDepthExceeded()
    {
        // Create nesting deeper than max depth
        var sb = new StringBuilder();
        int depth = 65;
        for (int i = 0; i < depth; i++)
        {
            sb.Append("block {\n");
        }

        for (int i = 0; i < depth; i++)
        {
            sb.Append("}\n");
        }

        var reader = CreateReader(sb.ToString());

        var ex = Record.Exception(() =>
        {
            byte[] b = Encoding.UTF8.GetBytes(sb.ToString());
            var r = new Utf8HclReader(b);
            while (r.Read()) { }
        });
        Assert.IsType<MaxRecursionDepthExceededException>(ex);
    }

    [Fact]
    public void BlockLabelThatLooksLikeNumber()
    {
        // String label "123" is valid
        var reader = CreateReader("resource \"123\" {\n}\n");
        Assert.True(reader.Read()); // BlockType
        Assert.True(reader.Read()); // BlockLabel
        Assert.Equal(HclTokenType.BlockLabel, reader.TokenType);
        Assert.Equal("123", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BlocksFollowedImmediatelyByAnother()
    {
        string hcl = "a {}\nb {}\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // BlockType "a"
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // BlockEnd

        Assert.True(reader.Read()); // BlockType "b"
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("b", Encoding.UTF8.GetString(reader.ValueSpan));
    }
}
