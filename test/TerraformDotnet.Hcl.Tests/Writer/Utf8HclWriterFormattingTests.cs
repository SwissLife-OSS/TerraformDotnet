using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Writer;

public sealed class Utf8HclWriterFormattingTests
{
    private static string Write(Action<Utf8HclWriter> action)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        action(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [Fact]
    public void TwoSpaceIndentation()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("outer");
            w.WriteAttributeName("x");
            w.WriteNumberValue(1);
            w.WriteAttributeEnd();
            w.WriteBlockEnd();
        });

        Assert.Contains("  x = 1\n", result);
    }

    [Fact]
    public void FourSpaceIndentationAtDepthTwo()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("a");
            w.WriteBlockStart("b");
            w.WriteAttributeName("x");
            w.WriteNumberValue(1);
            w.WriteAttributeEnd();
            w.WriteBlockEnd();
            w.WriteBlockEnd();
        });

        Assert.Contains("    x = 1\n", result);
    }

    [Fact]
    public void OpeningBraceOnSameLine()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "aws_instance", "main");
            w.WriteBlockEnd();
        });

        Assert.StartsWith("resource \"aws_instance\" \"main\" {\n", result);
    }

    [Fact]
    public void ClosingBraceOnOwnLine()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("test");
            w.WriteBlockEnd();
        });

        Assert.EndsWith("}\n", result);
    }

    [Fact]
    public void NoTrailingWhitespace()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "test", "main");
            w.WriteAttributeName("name");
            w.WriteStringValue("hello");
            w.WriteAttributeEnd();
            w.WriteBlockEnd();
        });

        string[] lines = result.Split('\n');
        foreach (string line in lines)
        {
            if (line.Length > 0)
            {
                Assert.False(line.EndsWith(' '), $"Trailing space on line: '{line}'");
            }
        }
    }

    [Fact]
    public void LfNewlines()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("test");
            w.WriteBlockEnd();
        });

        Assert.DoesNotContain("\r\n", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void CrLfNewlines()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var options = new HclWriterOptions { NewLineStyle = NewLineStyle.CrLf };
        using var writer = new Utf8HclWriter(buffer, options);

        writer.WriteBlockStart("test");
        writer.WriteBlockEnd();
        writer.Flush();

        string result = Encoding.UTF8.GetString(buffer.WrittenSpan);
        Assert.Contains("\r\n", result);
    }

    [Fact]
    public void EqualsAlignmentWithinBlock()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "test", "main");
            w.WriteAttributeName("ami", 10);           // pad = 10 (13-3)
            w.WriteStringValue("abc-123");
            w.WriteAttributeEnd();
            w.WriteAttributeName("instance_type", 0);  // pad = 0 (13-13)
            w.WriteStringValue("t2.micro");
            w.WriteAttributeEnd();
            w.WriteAttributeName("count", 8);          // pad = 8 (13-5)
            w.WriteNumberValue(3);
            w.WriteAttributeEnd();
            w.WriteBlockEnd();
        });

        string expected =
            "resource \"test\" \"main\" {\n" +
            "  ami           = \"abc-123\"\n" +
            "  instance_type = \"t2.micro\"\n" +
            "  count         = 3\n" +
            "}\n";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CommentPreservation()
    {
        string result = Write(w =>
        {
            w.WriteLineComment(" this is a line comment");
            w.WriteNewLine();
            w.WriteAttributeName("x");
            w.WriteNumberValue(1);
            w.WriteAttributeEnd();
        });

        Assert.Equal("// this is a line comment\nx = 1\n", result);
    }

    [Fact]
    public void HashCommentPreservation()
    {
        string result = Write(w =>
        {
            w.WriteHashComment(" hash comment");
            w.WriteNewLine();
        });

        Assert.Equal("# hash comment\n", result);
    }

    [Fact]
    public void BlockCommentPreservation()
    {
        string result = Write(w =>
        {
            w.WriteBlockComment(" block comment ");
            w.WriteNewLine();
        });

        Assert.Equal("/* block comment */\n", result);
    }

    [Fact]
    public void BytesWrittenAccumulates()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);

        Assert.Equal(0, writer.BytesWritten);
        writer.WriteRawString("hello");
        Assert.Equal(5, writer.BytesWritten);
        writer.WriteNewLine();
        Assert.Equal(6, writer.BytesWritten);
    }

    [Fact]
    public void StreamWriterFlushesOnDispose()
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8HclWriter(ms))
        {
            writer.WriteAttributeName("x");
            writer.WriteNumberValue(42);
            writer.WriteAttributeEnd();
        }

        string result = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Equal("x = 42\n", result);
    }
}
