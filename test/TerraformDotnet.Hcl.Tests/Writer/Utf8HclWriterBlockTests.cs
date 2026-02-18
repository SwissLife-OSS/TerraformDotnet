using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Writer;

public sealed class Utf8HclWriterBlockTests
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
    public void WriteBlockWithNoLabels()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("lifecycle");
            w.WriteBlockEnd();
        });

        Assert.Equal("lifecycle {\n}\n", result);
    }

    [Fact]
    public void WriteBlockWithOneLabel()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "aws_instance");
            w.WriteBlockEnd();
        });

        Assert.Equal("resource \"aws_instance\" {\n}\n", result);
    }

    [Fact]
    public void WriteBlockWithTwoLabels()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "aws_instance", "main");
            w.WriteBlockEnd();
        });

        Assert.Equal("resource \"aws_instance\" \"main\" {\n}\n", result);
    }

    [Fact]
    public void WriteNestedBlocks()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "aws_instance", "main");
            w.WriteBlockStart("lifecycle");
            w.WriteBlockEnd();
            w.WriteBlockEnd();
        });

        string expected =
            "resource \"aws_instance\" \"main\" {\n" +
            "  lifecycle {\n" +
            "  }\n" +
            "}\n";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void WriteDeeplyNestedBlocks()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("a");
            w.WriteBlockStart("b");
            w.WriteBlockStart("c");
            w.WriteBlockEnd();
            w.WriteBlockEnd();
            w.WriteBlockEnd();
        });

        string expected =
            "a {\n" +
            "  b {\n" +
            "    c {\n" +
            "    }\n" +
            "  }\n" +
            "}\n";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void WriteBlockLabelWithSpecialChars()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "has\"quote");
            w.WriteBlockEnd();
        });

        Assert.Equal("resource \"has\\\"quote\" {\n}\n", result);
    }

    [Fact]
    public void CurrentDepthTracksNesting()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);

        Assert.Equal(0, writer.CurrentDepth);
        writer.WriteBlockStart("a");
        Assert.Equal(1, writer.CurrentDepth);
        writer.WriteBlockStart("b");
        Assert.Equal(2, writer.CurrentDepth);
        writer.WriteBlockEnd();
        Assert.Equal(1, writer.CurrentDepth);
        writer.WriteBlockEnd();
        Assert.Equal(0, writer.CurrentDepth);
    }
}
