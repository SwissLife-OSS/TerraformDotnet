using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclNumberFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(-7, "-7")]
    [InlineData(1.5, "1.5")]
    [InlineData(-0.25, "-0.25")]
    [InlineData(1e21, "1000000000000000000000")]
    [InlineData(1e15, "1000000000000000")]
    [InlineData(1.5e-7, "0.00000015")]
    [InlineData(1.25e22, "12500000000000000000000")]
    [InlineData(-2.5e-10, "-0.00000000025")]
    [InlineData(123456789012345678, "123456789012345680")]
    public void FormatsWithoutExponent(double value, string expected)
    {
        Assert.Equal(expected, HclNumberFormatter.Format(value));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, "+Inf")]
    [InlineData(double.NegativeInfinity, "-Inf")]
    [InlineData(double.NaN, "NaN")]
    public void FormatsNonFiniteValues(double value, string expected)
    {
        Assert.Equal(expected, HclNumberFormatter.Format(value));
    }

    [Fact]
    public void NegativeZeroFormatsAsZero()
    {
        Assert.Equal("0", HclNumberFormatter.Format(-0.0));
    }

    [Fact]
    public void ValueToHclStringUsesTheFormatter()
    {
        Assert.Equal("1000000000000000000000", HclValue.FromNumber(1e21).ToHclString());
    }
}
