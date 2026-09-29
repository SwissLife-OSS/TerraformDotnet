using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Validation;

namespace TerraformDotnet.Tests.Validation;

public class TerraformLiteralTests
{
    [Fact]
    public void FactoriesSetKindAndValue()
    {
        Assert.Equal(new TerraformLiteral(HclLiteralKind.String, "a"), TerraformLiteral.FromString("a"));
        Assert.Equal(new TerraformLiteral(HclLiteralKind.Number, "1.5"), TerraformLiteral.FromNumber(1.5m));
        Assert.Equal(new TerraformLiteral(HclLiteralKind.Bool, "true"), TerraformLiteral.FromBool(true));
        Assert.Equal(new TerraformLiteral(HclLiteralKind.Bool, "false"), TerraformLiteral.FromBool(false));
    }

    [Fact]
    public void NullLiteralIsNull()
    {
        Assert.True(TerraformLiteral.Null.IsNull);
        Assert.Null(TerraformLiteral.Null.Value);
        Assert.False(TerraformLiteral.FromString("null").IsNull);
    }

    [Fact]
    public void NumbersUseInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-CH");

            var literal = TerraformLiteral.FromNumber(1.5m);

            Assert.Equal("1.5", literal.Value);
            Assert.True(literal.TryGetNumber(out var number));
            Assert.Equal(1.5m, number);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TryGetNumberFailsForNonNumbers()
    {
        Assert.False(TerraformLiteral.FromString("1").TryGetNumber(out _));
        Assert.False(TerraformLiteral.Null.TryGetNumber(out _));
        Assert.False(new TerraformLiteral(HclLiteralKind.Number, "abc").TryGetNumber(out _));
    }

    [Fact]
    public void TerraformEqualsFollowsTerraformSemantics()
    {
        Assert.True(TerraformLiteral.FromString("a").TerraformEquals(TerraformLiteral.FromString("a")));
        Assert.False(TerraformLiteral.FromString("a").TerraformEquals(TerraformLiteral.FromString("A")));
        Assert.True(TerraformLiteral.FromNumber(1m).TerraformEquals(TerraformLiteral.FromNumber(1.0m)));
        Assert.False(TerraformLiteral.FromString("1").TerraformEquals(TerraformLiteral.FromNumber(1m)));
        Assert.False(TerraformLiteral.FromBool(true).TerraformEquals(TerraformLiteral.FromString("true")));
        Assert.True(TerraformLiteral.Null.TerraformEquals(TerraformLiteral.Null));
        Assert.False(TerraformLiteral.Null.TerraformEquals(TerraformLiteral.FromString("")));
    }
}
