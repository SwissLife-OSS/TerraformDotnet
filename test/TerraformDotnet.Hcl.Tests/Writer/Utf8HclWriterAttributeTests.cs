using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Writer;

public sealed class Utf8HclWriterAttributeTests
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
    public void WriteStringAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("name");
            w.WriteStringValue("hello");
            w.WriteAttributeEnd();
        });

        Assert.Equal("name = \"hello\"\n", result);
    }

    [Fact]
    public void WriteStringWithEscapes()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("value");
            w.WriteStringValue("line1\nline2\ttab\"quote\\back");
            w.WriteAttributeEnd();
        });

        Assert.Equal("value = \"line1\\nline2\\ttab\\\"quote\\\\back\"\n", result);
    }

    [Fact]
    public void WriteIntAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("count");
            w.WriteNumberValue(42);
            w.WriteAttributeEnd();
        });

        Assert.Equal("count = 42\n", result);
    }

    [Fact]
    public void WriteLongAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("big");
            w.WriteNumberValue(9999999999L);
            w.WriteAttributeEnd();
        });

        Assert.Equal("big = 9999999999\n", result);
    }

    [Fact]
    public void WriteDoubleAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("rate");
            w.WriteNumberValue(3.14);
            w.WriteAttributeEnd();
        });

        Assert.Equal("rate = 3.14\n", result);
    }

    [Fact]
    public void WriteDecimalAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("price");
            w.WriteNumberValue(19.99m);
            w.WriteAttributeEnd();
        });

        Assert.Equal("price = 19.99\n", result);
    }

    [Fact]
    public void WriteBoolTrueAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("enabled");
            w.WriteBooleanValue(true);
            w.WriteAttributeEnd();
        });

        Assert.Equal("enabled = true\n", result);
    }

    [Fact]
    public void WriteBoolFalseAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("debug");
            w.WriteBooleanValue(false);
            w.WriteAttributeEnd();
        });

        Assert.Equal("debug = false\n", result);
    }

    [Fact]
    public void WriteNullAttribute()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("value");
            w.WriteNullValue();
            w.WriteAttributeEnd();
        });

        Assert.Equal("value = null\n", result);
    }

    [Fact]
    public void WriteAttributeWithAlignment()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("a", 4); // pad 4 to align
            w.WriteNumberValue(1);
            w.WriteAttributeEnd();
            w.WriteAttributeName("bbbbb", 0);
            w.WriteNumberValue(2);
            w.WriteAttributeEnd();
        });

        Assert.Equal("a     = 1\nbbbbb = 2\n", result);
    }

    [Fact]
    public void WriteAttributeInsideBlock()
    {
        string result = Write(w =>
        {
            w.WriteBlockStart("resource", "test", "main");
            w.WriteAttributeName("ami");
            w.WriteStringValue("abc-123");
            w.WriteAttributeEnd();
            w.WriteBlockEnd();
        });

        string expected =
            "resource \"test\" \"main\" {\n" +
            "  ami = \"abc-123\"\n" +
            "}\n";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AlignAttributesComputation()
    {
        var names = new List<string> { "a", "instance_type", "ami" };
        var padding = Utf8HclWriter.AlignAttributes(names);

        Assert.Equal(12, padding["a"]);            // 13 - 1
        Assert.Equal(0, padding["instance_type"]); // 13 - 13
        Assert.Equal(10, padding["ami"]);           // 13 - 3
    }
}
