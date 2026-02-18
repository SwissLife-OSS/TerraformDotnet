using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderCommentTests
{
    private static Utf8HclReader CreateReader(string hcl, bool readComments = true)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes, new HclReaderOptions { ReadComments = readComments });
    }

    [Fact]
    public void LineComment_DoubleSlash()
    {
        var reader = CreateReader("// this is a comment\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" this is a comment", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void LineComment_Hash()
    {
        var reader = CreateReader("# this is a comment\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" this is a comment", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BlockComment_SingleLine()
    {
        var reader = CreateReader("/* block comment */\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" block comment ", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BlockComment_MultiLine()
    {
        var reader = CreateReader("/* line1\n   line2\n   line3 */\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        string value = Encoding.UTF8.GetString(reader.ValueSpan);
        Assert.Contains("line1", value);
        Assert.Contains("line2", value);
        Assert.Contains("line3", value);
    }

    [Fact]
    public void Comment_AtEndOfAttributeLine()
    {
        var reader = CreateReader("x = 1 // comment\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
    }

    [Fact]
    public void Comment_BetweenBlocks()
    {
        string hcl = """
            a {}
            // between blocks
            b {}
            """;
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // BlockType a
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.True(reader.Read()); // BlockStart
        Assert.Equal(HclTokenType.BlockStart, reader.TokenType);
        Assert.True(reader.Read()); // BlockEnd
        Assert.Equal(HclTokenType.BlockEnd, reader.TokenType);
        Assert.True(reader.Read()); // Comment
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.True(reader.Read()); // BlockType b
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
    }

    [Fact]
    public void Comment_BeforeFirstBlock()
    {
        string hcl = "# header comment\nresource {}";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BlockType, reader.TokenType);
        Assert.Equal("resource", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void Comment_AfterLastBlock()
    {
        string hcl = "a {}\n// trailing comment";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); // BlockType
        Assert.True(reader.Read()); // BlockStart
        Assert.True(reader.Read()); // BlockEnd
        Assert.True(reader.Read()); // Comment
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
    }

    [Fact]
    public void EmptyLineComment_DoubleSlash()
    {
        var reader = CreateReader("//\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(0, reader.ValueSpan.Length);
    }

    [Fact]
    public void EmptyBlockComment()
    {
        var reader = CreateReader("/**/\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(0, reader.ValueSpan.Length);
    }

    [Fact]
    public void BlockComment_WithStarInside()
    {
        var reader = CreateReader("/* a * b */\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" a * b ", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BlockComment_WithSlashInside()
    {
        var reader = CreateReader("/* a / b */\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" a / b ", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void BlockComment_NestedOpeningNotSupported()
    {
        // In HCL, /* inside a block comment is not nested — first */ closes it
        var reader = CreateReader("/* outer /* inner */ rest\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" outer /* inner ", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void UnterminatedBlockComment_ThrowsSyntaxException()
    {
        var ex = Record.Exception(() =>
        {
            byte[] b = Encoding.UTF8.GetBytes("/* unterminated");
            var r = new Utf8HclReader(b);
            r.Read();
        });
        Assert.IsType<HclSyntaxException>(ex);
    }

    [Fact]
    public void Comment_WithUnicode()
    {
        var reader = CreateReader("// café ☕\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" café ☕", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void HashAndDoubleSlash_TreatedIdentically()
    {
        var reader1 = CreateReader("# hello\n");
        Assert.True(reader1.Read());
        string val1 = Encoding.UTF8.GetString(reader1.ValueSpan);

        var reader2 = CreateReader("// hello\n");
        Assert.True(reader2.Read());
        string val2 = Encoding.UTF8.GetString(reader2.ValueSpan);

        Assert.Equal(val1, val2);
    }

    [Fact]
    public void Comment_AtEofWithoutNewline()
    {
        var reader = CreateReader("// no trailing newline");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.Comment, reader.TokenType);
        Assert.Equal(" no trailing newline", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void Comments_SkippedWhenReadCommentsFalse()
    {
        string hcl = "// comment\nx = 1\n";
        var reader = CreateReader(hcl, readComments: false);

        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("x", Encoding.UTF8.GetString(reader.ValueSpan));
    }
}
