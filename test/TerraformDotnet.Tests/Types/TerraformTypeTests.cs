using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Types;

namespace TerraformDotnet.Tests.Types;

public class TerraformTypeTests
{
    [Fact]
    public void String_ReturnsCorrectKind()
    {
        var type = TerraformType.String;

        Assert.Equal(TerraformTypeKind.String, type.Kind);
    }

    [Fact]
    public void Number_ReturnsCorrectKind()
    {
        var type = TerraformType.Number;

        Assert.Equal(TerraformTypeKind.Number, type.Kind);
    }

    [Fact]
    public void Bool_ReturnsCorrectKind()
    {
        var type = TerraformType.Bool;

        Assert.Equal(TerraformTypeKind.Bool, type.Kind);
    }

    [Fact]
    public void Any_ReturnsCorrectKind()
    {
        var type = TerraformType.Any;

        Assert.Equal(TerraformTypeKind.Any, type.Kind);
    }

    [Fact]
    public void String_ToHcl_ReturnsString()
    {
        Assert.Equal("string", TerraformType.String.ToHcl());
    }

    [Fact]
    public void Number_ToHcl_ReturnsNumber()
    {
        Assert.Equal("number", TerraformType.Number.ToHcl());
    }

    [Fact]
    public void Bool_ToHcl_ReturnsBool()
    {
        Assert.Equal("bool", TerraformType.Bool.ToHcl());
    }

    [Fact]
    public void Any_ToHcl_ReturnsAny()
    {
        Assert.Equal("any", TerraformType.Any.ToHcl());
    }

    [Fact]
    public void PrimitiveSingletons_AreSameInstance()
    {
        Assert.Same(TerraformType.String, TerraformType.String);
        Assert.Same(TerraformType.Number, TerraformType.Number);
        Assert.Same(TerraformType.Bool, TerraformType.Bool);
        Assert.Same(TerraformType.Any, TerraformType.Any);
    }

    [Fact]
    public void PrimitiveEquality_SameKindAreEqual()
    {
        Assert.Equal(TerraformType.String, TerraformType.String);
        Assert.Equal(TerraformType.Number, TerraformType.Number);
    }

    [Fact]
    public void PrimitiveEquality_DifferentKindsAreNotEqual()
    {
        Assert.NotEqual(TerraformType.String, TerraformType.Number);
        Assert.NotEqual(TerraformType.Bool, TerraformType.Any);
    }

    [Fact]
    public void ListOfString_ReturnsCorrectKind()
    {
        var type = TerraformType.List(TerraformType.String);

        Assert.Equal(TerraformTypeKind.List, type.Kind);
        Assert.Equal(TerraformType.String, type.Element);
    }

    [Fact]
    public void ListOfString_ToHcl()
    {
        var type = TerraformType.List(TerraformType.String);

        Assert.Equal("list(string)", type.ToHcl());
    }

    [Fact]
    public void SetOfBool_ToHcl()
    {
        var type = TerraformType.Set(TerraformType.Bool);

        Assert.Equal("set(bool)", type.ToHcl());
    }

    [Fact]
    public void MapOfNumber_ToHcl()
    {
        var type = TerraformType.Map(TerraformType.Number);

        Assert.Equal("map(number)", type.ToHcl());
    }

    [Fact]
    public void MapOfString_ToHcl()
    {
        var type = TerraformType.Map(TerraformType.String);

        Assert.Equal("map(string)", type.ToHcl());
    }

    [Fact]
    public void CollectionEquality_SameAreEqual()
    {
        var a = TerraformType.List(TerraformType.String);
        var b = TerraformType.List(TerraformType.String);

        Assert.Equal(a, b);
    }

    [Fact]
    public void CollectionEquality_DifferentKindsNotEqual()
    {
        var list = TerraformType.List(TerraformType.String);
        var set = TerraformType.Set(TerraformType.String);

        Assert.NotEqual<TerraformType>(list, set);
    }

    [Fact]
    public void CollectionEquality_DifferentElementsNotEqual()
    {
        var a = TerraformType.List(TerraformType.String);
        var b = TerraformType.List(TerraformType.Number);

        Assert.NotEqual<TerraformType>(a, b);
    }

    [Fact]
    public void NestedCollection_ListOfMapOfString()
    {
        var type = TerraformType.List(TerraformType.Map(TerraformType.String));

        Assert.Equal("list(map(string))", type.ToHcl());
    }

    [Fact]
    public void ListOfAny_ToHcl()
    {
        var type = TerraformType.List(TerraformType.Any);

        Assert.Equal("list(any)", type.ToHcl());
    }

    [Fact]
    public void Object_EmptyFields_ToHcl()
    {
        var type = TerraformType.Object([]);

        Assert.Equal("object({})", type.ToHcl());
    }

    [Fact]
    public void Object_WithRequiredFields_ToHcl()
    {
        var type = TerraformType.Object([
            new TerraformObjectField("name", TerraformType.String),
            new TerraformObjectField("age", TerraformType.Number),
        ]);

        Assert.Equal("object({ name = string, age = number })", type.ToHcl());
    }

    [Fact]
    public void Object_WithOptionalField_ToHcl()
    {
        var type = TerraformType.Object([
            new TerraformObjectField("enabled", TerraformType.Bool, isOptional: true),
        ]);

        Assert.Equal("object({ enabled = optional(bool) })", type.ToHcl());
    }

    [Fact]
    public void Object_WithOptionalFieldAndDefault_ToHcl()
    {
        var defaultExpr = new HclLiteralExpression
        {
            Value = "true", Kind = HclLiteralKind.Bool,
        };

        var type = TerraformType.Object([
            new TerraformObjectField("enabled", TerraformType.Bool, isOptional: true, defaultExpr),
        ]);

        Assert.Equal("object({ enabled = optional(bool, true) })", type.ToHcl());
    }

    [Fact]
    public void Tuple_ToHcl()
    {
        var type = TerraformType.Tuple([TerraformType.String, TerraformType.Number]);

        Assert.Equal("tuple([string, number])", type.ToHcl());
    }

    [Fact]
    public void TupleEquality_SameElementsAreEqual()
    {
        var a = TerraformType.Tuple([TerraformType.String, TerraformType.Number]);
        var b = TerraformType.Tuple([TerraformType.String, TerraformType.Number]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void TupleEquality_DifferentElementsAreNotEqual()
    {
        var a = TerraformType.Tuple([TerraformType.String, TerraformType.Number]);
        var b = TerraformType.Tuple([TerraformType.String, TerraformType.Bool]);

        Assert.NotEqual<TerraformType>(a, b);
    }

    [Fact]
    public void ObjectEquality_SameFieldsAreEqual()
    {
        var a = TerraformType.Object([
            new TerraformObjectField("name", TerraformType.String),
        ]);
        var b = TerraformType.Object([
            new TerraformObjectField("name", TerraformType.String),
        ]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ObjectEquality_DifferentFieldsAreNotEqual()
    {
        var a = TerraformType.Object([
            new TerraformObjectField("name", TerraformType.String),
        ]);
        var b = TerraformType.Object([
            new TerraformObjectField("name", TerraformType.Number),
        ]);

        Assert.NotEqual<TerraformType>(a, b);
    }

    [Fact]
    public void ToString_ReturnsSameAsToHcl()
    {
        var type = TerraformType.List(TerraformType.String);

        Assert.Equal(type.ToHcl(), type.ToString());
    }

    [Fact]
    public void ObjectField_Equality()
    {
        var a = new TerraformObjectField("x", TerraformType.String);
        var b = new TerraformObjectField("x", TerraformType.String);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ObjectField_Inequality_DifferentName()
    {
        var a = new TerraformObjectField("x", TerraformType.String);
        var b = new TerraformObjectField("y", TerraformType.String);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ObjectField_Inequality_DifferentOptional()
    {
        var a = new TerraformObjectField("x", TerraformType.String, isOptional: false);
        var b = new TerraformObjectField("x", TerraformType.String, isOptional: true);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ObjectField_Null_NotEqual()
    {
        var field = new TerraformObjectField("x", TerraformType.String);

        Assert.False(field.Equals(null));
    }

    [Fact]
    public void PrimitiveType_EqualsNull_ReturnsFalse()
    {
        Assert.False(TerraformType.String.Equals((TerraformType?)null));
    }

    [Fact]
    public void PrimitiveType_EqualsObject_UsesBaseOverride()
    {
        object boxed = TerraformType.String;

        Assert.True(TerraformType.String.Equals(boxed));
    }
}
