using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Writer;

public sealed class Utf8HclWriterCollectionTests
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
    public void WriteEmptyTuple()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("list");
            w.WriteTupleStart();
            w.WriteTupleEnd();
            w.WriteAttributeEnd();
        });

        Assert.Equal("list = []\n", result);
    }

    [Fact]
    public void WriteTupleWithElements()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("list");
            w.WriteTupleStart();
            w.WriteStringValue("a");
            w.WriteComma();
            w.WriteStringValue("b");
            w.WriteComma();
            w.WriteStringValue("c");
            w.WriteTupleEnd();
            w.WriteAttributeEnd();
        });

        Assert.Equal("list = [\"a\", \"b\", \"c\"]\n", result);
    }

    [Fact]
    public void WriteEmptyObject()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("obj");
            w.WriteObjectStart();
            w.WriteObjectEnd();
            w.WriteAttributeEnd();
        });

        Assert.Equal("obj = {}\n", result);
    }

    [Fact]
    public void WriteObjectWithElements()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("tags");
            w.WriteObjectStart();
            w.WriteNewLine();
            w.CurrentDepth_Increment();
            w.WriteIndent();
            w.WriteRawString("Name");
            w.WriteEquals();
            w.WriteStringValue("main");
            w.WriteNewLine();
            w.CurrentDepth_Decrement();
            w.WriteObjectEnd();
            w.WriteAttributeEnd();
        });

        Assert.Equal("tags = {\n  Name = \"main\"\n}\n", result);
    }

    [Fact]
    public void WriteNestedCollections()
    {
        string result = Write(w =>
        {
            w.WriteAttributeName("value");
            w.WriteTupleStart();
            w.WriteTupleStart();
            w.WriteNumberValue(1);
            w.WriteComma();
            w.WriteNumberValue(2);
            w.WriteTupleEnd();
            w.WriteComma();
            w.WriteTupleStart();
            w.WriteNumberValue(3);
            w.WriteTupleEnd();
            w.WriteTupleEnd();
            w.WriteAttributeEnd();
        });

        Assert.Equal("value = [[1, 2], [3]]\n", result);
    }
}
