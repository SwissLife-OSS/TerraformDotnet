using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Types;

namespace TerraformDotnet.Tests.Types;

public class TerraformTypeParserTests
{
    /// <summary>
    /// Parses a type expression from HCL. In Terraform, <c>type = string</c> parses
    /// as an HclVariableExpression or HclFunctionCallExpression.
    /// </summary>
    private static TerraformType ParseType(string typeExpression)
    {
        var hcl = $"x = {typeExpression}\n";
        var file = HclFile.Load(Encoding.UTF8.GetBytes(hcl));
        var expr = file.Body.Attributes[0].Value;

        return TerraformType.Parse(expr);
    }

    [Fact]
    public void Parse_String()
    {
        var type = ParseType("string");

        Assert.Equal(TerraformTypeKind.String, type.Kind);
        Assert.Equal(TerraformType.String, type);
    }

    [Fact]
    public void Parse_Number()
    {
        var type = ParseType("number");

        Assert.Equal(TerraformTypeKind.Number, type.Kind);
    }

    [Fact]
    public void Parse_Bool()
    {
        var type = ParseType("bool");

        Assert.Equal(TerraformTypeKind.Bool, type.Kind);
    }

    [Fact]
    public void Parse_Any()
    {
        var type = ParseType("any");

        Assert.Equal(TerraformTypeKind.Any, type.Kind);
    }

    [Fact]
    public void Parse_ListOfString()
    {
        var type = ParseType("list(string)");

        Assert.IsType<TerraformCollectionType>(type);
        var collection = (TerraformCollectionType)type;
        Assert.Equal(TerraformTypeKind.List, collection.Kind);
        Assert.Equal(TerraformType.String, collection.Element);
    }

    [Fact]
    public void Parse_SetOfBool()
    {
        var type = ParseType("set(bool)");

        Assert.IsType<TerraformCollectionType>(type);
        var collection = (TerraformCollectionType)type;
        Assert.Equal(TerraformTypeKind.Set, collection.Kind);
        Assert.Equal(TerraformType.Bool, collection.Element);
    }

    [Fact]
    public void Parse_MapOfNumber()
    {
        var type = ParseType("map(number)");

        Assert.IsType<TerraformCollectionType>(type);
        var collection = (TerraformCollectionType)type;
        Assert.Equal(TerraformTypeKind.Map, collection.Kind);
        Assert.Equal(TerraformType.Number, collection.Element);
    }

    [Fact]
    public void Parse_MapOfString()
    {
        var type = ParseType("map(string)");

        var expected = TerraformType.Map(TerraformType.String);
        Assert.Equal(expected, type);
    }

    [Fact]
    public void Parse_ListOfAny()
    {
        var type = ParseType("list(any)");

        var expected = TerraformType.List(TerraformType.Any);
        Assert.Equal(expected, type);
    }

    [Fact]
    public void Parse_ObjectWithSimpleFields()
    {
        var type = ParseType("object({ name = string, age = number })");

        Assert.IsType<TerraformObjectType>(type);
        var obj = (TerraformObjectType)type;
        Assert.Equal(2, obj.Fields.Count);
        Assert.Equal("name", obj.Fields[0].Name);
        Assert.Equal(TerraformType.String, obj.Fields[0].Type);
        Assert.False(obj.Fields[0].IsOptional);
        Assert.Equal("age", obj.Fields[1].Name);
        Assert.Equal(TerraformType.Number, obj.Fields[1].Type);
        Assert.False(obj.Fields[1].IsOptional);
    }

    [Fact]
    public void Parse_ObjectWithOptionalFieldNoDefault()
    {
        var type = ParseType("object({ enabled = optional(bool) })");

        Assert.IsType<TerraformObjectType>(type);
        var obj = (TerraformObjectType)type;
        Assert.Single(obj.Fields);
        Assert.Equal("enabled", obj.Fields[0].Name);
        Assert.Equal(TerraformType.Bool, obj.Fields[0].Type);
        Assert.True(obj.Fields[0].IsOptional);
        Assert.Null(obj.Fields[0].Default);
    }

    [Fact]
    public void Parse_ObjectWithOptionalFieldAndBoolDefault()
    {
        var type = ParseType("object({ enabled = optional(bool, true) })");

        Assert.IsType<TerraformObjectType>(type);
        var obj = (TerraformObjectType)type;
        Assert.Single(obj.Fields);
        Assert.True(obj.Fields[0].IsOptional);
        Assert.NotNull(obj.Fields[0].Default);
        Assert.IsType<HclLiteralExpression>(obj.Fields[0].Default);
    }

    [Fact]
    public void Parse_ObjectWithOptionalFieldAndStringDefault()
    {
        var type = ParseType("object({ name = optional(string, \"default\") })");

        Assert.IsType<TerraformObjectType>(type);
        var obj = (TerraformObjectType)type;
        Assert.Single(obj.Fields);
        Assert.True(obj.Fields[0].IsOptional);
        Assert.NotNull(obj.Fields[0].Default);
        var lit = Assert.IsType<HclLiteralExpression>(obj.Fields[0].Default);
        Assert.Equal("default", lit.Value);
    }

    [Fact]
    public void Parse_ObjectWithMixedRequiredAndOptionalFields()
    {
        var type = ParseType(
            "object({ name = string, enabled = optional(bool, true), count = optional(number) })");

        Assert.IsType<TerraformObjectType>(type);
        var obj = (TerraformObjectType)type;
        Assert.Equal(3, obj.Fields.Count);
        Assert.False(obj.Fields[0].IsOptional); // name
        Assert.True(obj.Fields[1].IsOptional);  // enabled
        Assert.True(obj.Fields[2].IsOptional);  // count
        Assert.NotNull(obj.Fields[1].Default);  // enabled has default
        Assert.Null(obj.Fields[2].Default);     // count has no default
    }

    [Fact]
    public void Parse_SetOfObjectWithNestedOptional()
    {
        var type = ParseType(
            "set(object({ names = list(string), flag = optional(bool, false) }))");

        Assert.IsType<TerraformCollectionType>(type);
        var collection = (TerraformCollectionType)type;
        Assert.Equal(TerraformTypeKind.Set, collection.Kind);

        var innerObj = Assert.IsType<TerraformObjectType>(collection.Element);
        Assert.Equal(2, innerObj.Fields.Count);
        Assert.Equal("names", innerObj.Fields[0].Name);
        Assert.Equal(TerraformType.List(TerraformType.String), innerObj.Fields[0].Type);
        Assert.False(innerObj.Fields[0].IsOptional);
        Assert.Equal("flag", innerObj.Fields[1].Name);
        Assert.Equal(TerraformType.Bool, innerObj.Fields[1].Type);
        Assert.True(innerObj.Fields[1].IsOptional);
    }

    [Fact]
    public void Parse_TupleOfStringAndNumber()
    {
        var type = ParseType("tuple([string, number])");

        Assert.IsType<TerraformTupleType>(type);
        var tuple = (TerraformTupleType)type;
        Assert.Equal(2, tuple.Elements.Count);
        Assert.Equal(TerraformType.String, tuple.Elements[0]);
        Assert.Equal(TerraformType.Number, tuple.Elements[1]);
    }

    [Fact]
    public void Parse_ComplexNestedObject_AlertType()
    {
        var type = ParseType("""
            object({
                enabled     = optional(bool, true)
                severity    = optional(number, 3)
                frequency   = optional(string, "PT5M")
                window_size = optional(string, "PT15M")
                threshold   = optional(number, 80)
                operator    = optional(string, "GreaterThan")
            })
            """);

        Assert.IsType<TerraformObjectType>(type);
        var obj = (TerraformObjectType)type;
        Assert.Equal(6, obj.Fields.Count);
        Assert.All(obj.Fields, f => Assert.True(f.IsOptional));
        Assert.All(obj.Fields, f => Assert.NotNull(f.Default));
    }

    [Fact]
    public void Parse_ObjectWithNestedDimensionsList()
    {
        var type = ParseType("""
            object({
                enabled    = optional(bool, true)
                dimensions = optional(list(object({
                    name     = string
                    operator = optional(string, "Include")
                    values   = list(string)
                })), [])
            })
            """);

        Assert.IsType<TerraformObjectType>(type);
        var obj = (TerraformObjectType)type;
        Assert.Equal(2, obj.Fields.Count);

        var dimensionsField = obj.Fields[1];
        Assert.Equal("dimensions", dimensionsField.Name);
        Assert.True(dimensionsField.IsOptional);
        Assert.NotNull(dimensionsField.Default);

        var dimType = Assert.IsType<TerraformCollectionType>(dimensionsField.Type);
        Assert.Equal(TerraformTypeKind.List, dimType.Kind);
        var dimObj = Assert.IsType<TerraformObjectType>(dimType.Element);
        Assert.Equal(3, dimObj.Fields.Count);
    }

    [Fact]
    public void Parse_NullExpression_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TerraformType.Parse(null!));
    }

    [Fact]
    public void Parse_UnknownPrimitive_Throws()
    {
        Assert.Throws<FormatException>(() => ParseType("foobar"));
    }

    [Fact]
    public void Parse_UnknownFunction_Throws()
    {
        Assert.Throws<FormatException>(() => ParseType("unknown(string)"));
    }

    [Fact]
    public void Parse_TopLevelOptional_Throws()
    {
        Assert.Throws<FormatException>(() => ParseType("optional(string)"));
    }
}
