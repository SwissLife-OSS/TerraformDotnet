using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests literal expressions in attribute values: numbers, booleans, null, strings.
/// </summary>
public sealed class Utf8HclReaderExpressionLiteralTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    [Fact]
    public void IntegerLiteral()
    {
        var reader = CreateReader("x = 42\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal(42, reader.GetInt32());
    }

    [Fact]
    public void FloatLiteral()
    {
        var reader = CreateReader("x = 3.14\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal(3.14, reader.GetDouble(), 0.001);
    }

    [Fact]
    public void ScientificNotation()
    {
        var reader = CreateReader("x = 1e10\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal(1e10, reader.GetDouble(), 1.0);
    }

    [Fact]
    public void BoolTrue()
    {
        var reader = CreateReader("x = true\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BoolLiteral, reader.TokenType);
        Assert.True(reader.GetBoolean());
    }

    [Fact]
    public void BoolFalse()
    {
        var reader = CreateReader("x = false\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.BoolLiteral, reader.TokenType);
        Assert.False(reader.GetBoolean());
    }

    [Fact]
    public void NullLiteral()
    {
        var reader = CreateReader("x = null\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NullLiteral, reader.TokenType);
        Assert.Null(reader.GetString());
    }

    [Fact]
    public void StringLiteral()
    {
        var reader = CreateReader("x = \"hello\"\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
        Assert.Equal("hello", reader.GetString());
    }

    [Fact]
    public void TrueAsAttributeName()
    {
        // 'true' used as attribute name in body context → AttributeName
        var reader = CreateReader("true = 1\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("true", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
    }

    [Fact]
    public void NullAsAttributeName()
    {
        var reader = CreateReader("null = \"value\"\n");
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.Equal("null", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void NegativeNumberIsUnaryMinusPlusLiteral()
    {
        var reader = CreateReader("x = -5\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // Minus
        Assert.Equal(HclTokenType.Minus, reader.TokenType);
        Assert.True(reader.Read()); // NumberLiteral
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal(5, reader.GetInt32());
    }

    [Fact]
    public void MultipleAttributesWithDifferentLiteralTypes()
    {
        string hcl = "a = 42\nb = \"hello\"\nc = true\nd = null\ne = 3.14\n";
        var reader = CreateReader(hcl);

        Assert.True(reader.Read()); Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);

        Assert.True(reader.Read()); Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);

        Assert.True(reader.Read()); Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); Assert.Equal(HclTokenType.BoolLiteral, reader.TokenType);

        Assert.True(reader.Read()); Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); Assert.Equal(HclTokenType.NullLiteral, reader.TokenType);

        Assert.True(reader.Read()); Assert.Equal(HclTokenType.AttributeName, reader.TokenType);
        Assert.True(reader.Read()); Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
    }

    [Fact]
    public void ZeroInteger()
    {
        var reader = CreateReader("x = 0\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal(0, reader.GetInt32());
    }

    [Fact]
    public void LargeInteger()
    {
        var reader = CreateReader("x = 999999999999\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal(999999999999L, reader.GetInt64());
    }
}
