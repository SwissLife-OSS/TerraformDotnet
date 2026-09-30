using System.Collections.ObjectModel;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclValueFactoryTests
{
    [Fact]
    public void FromObjectCopiesTheEntries()
    {
        var entries = new Dictionary<string, HclValue> { ["a"] = HclValue.FromString("one") };

        var value = HclValue.FromObject(entries);
        entries["a"] = HclValue.FromString("two");
        entries["b"] = HclValue.FromString("three");

        Assert.Equal("one", value.ObjectValue["a"].StringValue);
        Assert.False(value.ObjectValue.ContainsKey("b"));
    }

    [Fact]
    public void WrapObjectUsesTheGivenDictionaryWithoutCopying()
    {
        var entries = new Dictionary<string, HclValue> { ["a"] = HclValue.FromString("one") };
        var view = new ReadOnlyDictionary<string, HclValue>(entries);

        var value = HclValue.WrapObject(view);
        entries["a"] = HclValue.FromString("two");

        Assert.Equal(HclValueType.Object, value.Type);
        Assert.Same(view, value.ObjectValue);
        Assert.Equal("two", value.ObjectValue["a"].StringValue);
    }

    [Fact]
    public void WrapObjectRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => HclValue.WrapObject(null!));
    }

    [Fact]
    public void UnknownWithoutArgumentsHasEmptyReadOnlyArguments()
    {
        var first = HclValue.Unknown("first");
        var second = HclValue.Unknown("second");

        Assert.Empty(first.UnknownArgs);
        Assert.Empty(second.UnknownArgs);
        Assert.Equal("first", first.UnknownSource);
        Assert.Equal("second", second.UnknownSource);
        Assert.True(((System.Collections.IList)first.UnknownArgs).IsReadOnly);
    }

    [Fact]
    public void UnknownKeepsItsArguments()
    {
        var argument = HclValue.FromNumber(1);

        var value = HclValue.Unknown("f", [argument]);

        Assert.Same(argument, Assert.Single(value.UnknownArgs));
    }
}
