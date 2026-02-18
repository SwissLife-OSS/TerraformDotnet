using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderNumberTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    /// <summary>
    /// Reads "x = {number}" and returns the number token string.
    /// </summary>
    private static string ReadNumberToken(string numberLiteral)
    {
        var reader = CreateReader($"x = {numberLiteral}\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // NumberLiteral
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        return Encoding.UTF8.GetString(reader.ValueSpan);
    }

    [Fact]
    public void Integer() => Assert.Equal("42", ReadNumberToken("42"));

    [Fact]
    public void Zero() => Assert.Equal("0", ReadNumberToken("0"));

    [Fact]
    public void Float() => Assert.Equal("3.14", ReadNumberToken("3.14"));

    [Fact]
    public void ScientificNotation_Lower() => Assert.Equal("1e10", ReadNumberToken("1e10"));

    [Fact]
    public void ScientificNotation_Upper() => Assert.Equal("1E10", ReadNumberToken("1E10"));

    [Fact]
    public void ScientificWithPositiveSign() => Assert.Equal("1e+10", ReadNumberToken("1e+10"));

    [Fact]
    public void ScientificWithNegativeSign() => Assert.Equal("1e-10", ReadNumberToken("1e-10"));

    [Fact]
    public void FloatWithExponent() => Assert.Equal("1.5e10", ReadNumberToken("1.5e10"));

    [Fact]
    public void LargeInteger() => Assert.Equal("999999999999999999", ReadNumberToken("999999999999999999"));

    [Fact]
    public void SmallFloat() => Assert.Equal("0.001", ReadNumberToken("0.001"));

    [Fact]
    public void LeadingZeros()
    {
        // Leading zeros are parsed as a single number token
        Assert.Equal("007", ReadNumberToken("007"));
    }

    [Fact]
    public void ZeroExponent() => Assert.Equal("1e0", ReadNumberToken("1e0"));

    [Fact]
    public void LargeExponent() => Assert.Equal("1e999", ReadNumberToken("1e999"));

    [Fact]
    public void NegativeNumber_IsUnaryMinusThenNumber()
    {
        var reader = CreateReader("x = -5\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // Minus
        Assert.Equal(HclTokenType.Minus, reader.TokenType);
        Assert.True(reader.Read()); // NumberLiteral
        Assert.Equal(HclTokenType.NumberLiteral, reader.TokenType);
        Assert.Equal("5", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void GetInt32_Returns42()
    {
        var reader = CreateReader("x = 42\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(42, reader.GetInt32());
    }

    [Fact]
    public void GetDouble_Returns3Point14()
    {
        var reader = CreateReader("x = 3.14\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(3.14, reader.GetDouble(), precision: 10);
    }

    [Fact]
    public void GetInt64_ReturnsLargeNumber()
    {
        var reader = CreateReader("x = 999999999999\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(999999999999L, reader.GetInt64());
    }
}
