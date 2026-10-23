using TerraformDotnet.Evaluation;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;
using TerraformDotnet.Types;

namespace TerraformDotnet.Tests.Evaluation;

public class TerraformTypeConverterTests
{
    private static readonly HclEvaluator Evaluator = new(new HclEvaluatorOptions
    {
        FunctionResolver = TerraformFunctions.Default,
    });

    private static HclValue Value(string expression)
        => Evaluator.Evaluate(HclExpression.Parse(expression), new HclEvaluationContext());

    private static TerraformType Type(string type) => TerraformType.Parse(HclExpression.Parse(type));

    private static HclValue Convert(string type, string value)
    {
        Assert.True(
            TerraformTypeConverter.TryConvert(Value(value), Type(type), out var result, out var error),
            error);

        return result;
    }

    private static string Fail(string type, string value)
    {
        Assert.False(TerraformTypeConverter.TryConvert(Value(value), Type(type), out _, out var error));

        return error!;
    }

    [Theory]
    [InlineData("string", "\"a\"", "\"a\"")]
    [InlineData("string", "1", "\"1\"")]
    [InlineData("string", "1.5", "\"1.5\"")]
    [InlineData("string", "true", "\"true\"")]
    [InlineData("number", "\"42\"", "42")]
    [InlineData("number", "\"1e2\"", "100")]
    [InlineData("number", "7", "7")]
    [InlineData("bool", "\"true\"", "true")]
    [InlineData("bool", "\"false\"", "false")]
    [InlineData("bool", "\"1\"", "true")]
    [InlineData("bool", "\"0\"", "false")]
    public void ConvertsPrimitives(string type, string value, string expected)
    {
        Assert.Equal(Value(expected), Convert(type, value));
    }

    [Theory]
    [InlineData("number", "\"abc\"")]
    [InlineData("number", "\" 1\"")]
    [InlineData("number", "true")]
    [InlineData("bool", "\"TRUE\"")]
    [InlineData("bool", "1")]
    [InlineData("string", "[1]")]
    [InlineData("string", "{a = 1}")]
    [InlineData("list(string)", "\"a\"")]
    [InlineData("list(string)", "{a = \"b\"}")]
    [InlineData("map(string)", "[\"a\"]")]
    [InlineData("object({a = string})", "[\"a\"]")]
    public void RejectsImpossibleConversions(string type, string value)
    {
        Assert.NotEmpty(Fail(type, value));
    }

    [Fact]
    public void NullAndUnknownPassThroughAnyType()
    {
        Assert.Equal(HclValue.Null, Convert("string", "null"));
        Assert.Equal(HclValue.Null, Convert("object({a = string})", "null"));
        Assert.Equal(HclValue.Null, Convert("list(number)", "null"));

        var unknown = HclValue.Unknown("x");
        Assert.True(TerraformTypeConverter.TryConvert(unknown, Type("number"), out var result, out _));
        Assert.Equal(unknown, result);
    }

    [Fact]
    public void AnyAndMissingTypesKeepTheValue()
    {
        var value = Value("{a = [1, \"x\"]}");

        Assert.True(TerraformTypeConverter.TryConvert(value, TerraformType.Any, out var any, out _));
        Assert.True(TerraformTypeConverter.TryConvert(value, null, out var none, out _));
        Assert.Equal(value, any);
        Assert.Equal(value, none);
    }

    [Fact]
    public void ConvertsElementsOfListsAndMaps()
    {
        Assert.Equal(Value("[\"1\", \"true\", \"x\"]"), Convert("list(string)", "[1, true, \"x\"]"));
        Assert.Equal(Value("{k = 3}"), Convert("map(number)", "{k = \"3\"}"));
        Assert.Equal(Value("[1, 2]"), Convert("list(number)", "[\"1\", 2]"));
    }

    [Fact]
    public void SetsAreDistinctAndOrdered()
    {
        Assert.Equal(Value("[\"a\", \"b\"]"), Convert("set(string)", "[\"b\", \"a\", \"b\"]"));
        Assert.Equal(Value("[1, 2, 3]"), Convert("set(number)", "[3, 1, 2, 1]"));
    }

    [Fact]
    public void AnyElementsAreUnifiedLikeTerraform()
    {
        Assert.Equal(Value("[\"1\", \"a\"]"), Convert("list(any)", "[1, \"a\"]"));
        Assert.Equal(Value("[1, 2]"), Convert("list(any)", "[1, 2]"));
    }

    [Fact]
    public void ObjectsDropUndeclaredAttributes()
    {
        var result = Convert("object({a = string})", "{a = 1, z = 9}");

        Assert.Equal(Value("{a = \"1\"}"), result);
    }

    [Fact]
    public void OptionalAttributesReceiveDefaultsOrNull()
    {
        var type = "object({a = string, b = optional(number, 5), c = optional(string)})";

        Assert.Equal(Value("{a = \"x\", b = 5, c = null}"), Convert(type, "{a = \"x\"}"));
        Assert.Equal(Value("{a = \"x\", b = 5, c = null}"), Convert(type, "{a = \"x\", b = null}"));
        Assert.Equal(Value("{a = \"x\", b = 7, c = \"q\"}"), Convert(type, "{a = \"x\", b = \"7\", c = \"q\"}"));
    }

    [Fact]
    public void OptionalDefaultsApplyInsideCollections()
    {
        var result = Convert(
            "list(object({x = optional(string, \"d\")}))",
            "[{}, {x = null}, {x = \"q\"}]");

        Assert.Equal(Value("[{x = \"d\"}, {x = \"d\"}, {x = \"q\"}]"), result);
    }

    [Fact]
    public void OptionalDefaultsMayCallFunctions()
    {
        var result = Convert("object({a = optional(list(string), tolist([\"x\"]))})", "{}");

        Assert.Equal(Value("{a = [\"x\"]}"), result);
    }

    [Fact]
    public void RequiredAttributesMustBePresentButMayBeNull()
    {
        Assert.Contains("\"a\" is required", Fail("object({a = string})", "{}"), StringComparison.Ordinal);
        Assert.Equal(Value("{a = null}"), Convert("object({a = string})", "{a = null}"));
    }

    [Fact]
    public void ErrorsNameTheOffendingPath()
    {
        Assert.Contains("value.lock.kind", Fail("object({lock = object({kind = number})})", "{lock = {kind = \"x\"}}"), StringComparison.Ordinal);
        Assert.Contains("value[1]", Fail("list(number)", "[1, \"x\"]"), StringComparison.Ordinal);
    }

    [Fact]
    public void TuplesRequireMatchingLengthAndTypes()
    {
        Assert.Equal(Value("[\"1\", 2]"), Convert("tuple([string, number])", "[1, \"2\"]"));
        Assert.Contains("2 elements", Fail("tuple([string, number])", "[1]"), StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectsConvertToMapsOfTheCommonType()
    {
        Assert.Equal(Value("{a = \"1\", b = \"x\"}"), Convert("map(any)", "{a = 1, b = \"x\"}"));
    }

    [Fact]
    public void ConvertsTheDeclaredDefaultOfAModuleVariable()
    {
        var module = TerraformModule.LoadFromContent("""
            variable "acl" {
              type    = object({ action = optional(string, "Deny") })
              default = {}
            }
            """u8);
        var variable = module.Variables[0];
        var value = Evaluator.Evaluate(variable.Default!, new HclEvaluationContext());

        Assert.True(TerraformTypeConverter.TryConvert(value, variable.Type, out var result, out _));
        Assert.Equal(Value("{action = \"Deny\"}"), result);
    }
}
