using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Types;

namespace TerraformDotnet.Tests.Types;

/// <summary>
/// Tests round-trip: construct type → ToHcl() → parse back → assert equal.
/// </summary>
public class TerraformTypeHclTests
{
    private static TerraformType RoundTrip(TerraformType type)
    {
        var hcl = type.ToHcl();
        var file = HclFile.Load(Encoding.UTF8.GetBytes($"x = {hcl}\n"));
        var expr = file.Body.Attributes[0].Value;

        return TerraformType.Parse(expr);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("bool")]
    [InlineData("any")]
    public void RoundTrip_Primitive(string typeName)
    {
        var original = typeName switch
        {
            "string" => TerraformType.String,
            "number" => TerraformType.Number,
            "bool" => TerraformType.Bool,
            "any" => TerraformType.Any,
            _ => throw new ArgumentException(typeName),
        };

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_ListOfString()
    {
        var original = TerraformType.List(TerraformType.String);

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_MapOfString()
    {
        var original = TerraformType.Map(TerraformType.String);

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_SetOfNumber()
    {
        var original = TerraformType.Set(TerraformType.Number);

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_ListOfAny()
    {
        var original = TerraformType.List(TerraformType.Any);

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_TupleOfStringAndNumber()
    {
        var original = TerraformType.Tuple([TerraformType.String, TerraformType.Number]);

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_ObjectWithRequiredFields()
    {
        var original = TerraformType.Object([
            new TerraformObjectField("name", TerraformType.String),
            new TerraformObjectField("count", TerraformType.Number),
        ]);

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_ObjectWithOptionalField()
    {
        var original = TerraformType.Object([
            new TerraformObjectField("enabled", TerraformType.Bool, isOptional: true),
        ]);

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_NestedListOfMapOfString()
    {
        var original = TerraformType.List(TerraformType.Map(TerraformType.String));

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_SetOfObjectWithOptionalFields()
    {
        var original = TerraformType.Set(TerraformType.Object([
            new TerraformObjectField("name", TerraformType.String),
            new TerraformObjectField("active", TerraformType.Bool, isOptional: true),
        ]));

        var roundTripped = RoundTrip(original);

        Assert.Equal(original, roundTripped);
    }
}
