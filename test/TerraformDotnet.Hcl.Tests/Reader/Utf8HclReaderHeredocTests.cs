using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderHeredocTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    [Fact]
    public void BasicHeredoc()
    {
        string hcl = "x = <<EOF\nhello\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // StringLiteral
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        Assert.Equal(StringLiteralStyle.Heredoc, reader.StringStyle);
        string value = Encoding.UTF8.GetString(reader.ValueSpan);
        Assert.Equal("hello\n", value);
    }

    [Fact]
    public void IndentedHeredoc()
    {
        string hcl = "x = <<-EOF\n  hello\n  world\n  EOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // StringLiteral
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        Assert.Equal(StringLiteralStyle.IndentedHeredoc, reader.StringStyle);
    }

    [Fact]
    public void HeredocWithMultipleLines()
    {
        string hcl = "x = <<EOF\nline1\nline2\nline3\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        string value = Encoding.UTF8.GetString(reader.ValueSpan);
        Assert.Contains("line1", value);
        Assert.Contains("line2", value);
        Assert.Contains("line3", value);
    }

    [Fact]
    public void HeredocWithEmptyLines()
    {
        string hcl = "x = <<EOF\nline1\n\nline3\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        string value = Encoding.UTF8.GetString(reader.ValueSpan);
        Assert.Contains("line1\n\nline3", value);
    }

    [Fact]
    public void HeredocWithCustomMarker()
    {
        string hcl = "x = <<POLICY\ncontent\nPOLICY\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        Assert.Contains("content", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void HeredocMarkerInsideContent_NotTerminator()
    {
        // "EOF" appears inside the content but not alone on its own line
        string hcl = "x = <<EOF\nthe word EOF appears here\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        string value = Encoding.UTF8.GetString(reader.ValueSpan);
        Assert.Contains("the word EOF appears here", value);
    }

    [Fact]
    public void HeredocNoContent()
    {
        // Marker immediately on next line = empty content
        string hcl = "x = <<EOF\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        Assert.Equal(0, reader.ValueSpan.Length);
    }

    [Fact]
    public void UnterminatedHeredoc_Throws()
    {
        string hcl = "x = <<EOF\nhello\nno closing marker";
        var ex = Record.Exception(() =>
        {
            byte[] b = Encoding.UTF8.GetBytes(hcl);
            var r = new Utf8HclReader(b);
            while (r.Read()) { }
        });
        Assert.IsType<HclSyntaxException>(ex);
    }

    [Fact]
    public void HeredocContainingDoubleAngleBrackets()
    {
        // Content containing << should not confuse the parser
        string hcl = "x = <<EOF\nvalue << shift\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Contains("value << shift", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void HeredocWithInterpolation()
    {
        // Interpolation within heredoc is preserved as raw bytes
        string hcl = "x = <<EOF\nHello ${name}\nEOF\n";
        var reader = CreateReader(hcl);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        string value = Encoding.UTF8.GetString(reader.ValueSpan);
        Assert.Contains("${name}", value);
    }

    [Fact]
    public void IndentedHeredocDecoder_StripsCommonWhitespace()
    {
        // Test the static decode helper directly
        byte[] raw = Encoding.UTF8.GetBytes("  hello\n  world\n");
        string result = Utf8HclReader.DecodeIndentedHeredoc(raw);
        Assert.Equal("hello\nworld\n", result);
    }

    [Fact]
    public void IndentedHeredocDecoder_MixedIndentation()
    {
        byte[] raw = Encoding.UTF8.GetBytes("    deep\n  shallow\n");
        string result = Utf8HclReader.DecodeIndentedHeredoc(raw);
        Assert.Equal("  deep\nshallow\n", result);
    }
}
