using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Tests.Common;

public sealed class MarkTests
{
    [Fact]
    public void DefaultMark_HasZeroValues()
    {
        var mark = default(Mark);

        Assert.Equal(0, mark.Offset);
        Assert.Equal(0, mark.Line);
        Assert.Equal(0, mark.Column);
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var mark = new Mark(offset: 42, line: 3, column: 7);

        Assert.Equal(42, mark.Offset);
        Assert.Equal(3, mark.Line);
        Assert.Equal(7, mark.Column);
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Mark(10, 2, 5);
        var b = new Mark(10, 2, 5);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equals_DifferentOffset_ReturnsFalse()
    {
        var a = new Mark(10, 2, 5);
        var b = new Mark(11, 2, 5);

        Assert.False(a.Equals(b));
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equals_DifferentLine_ReturnsFalse()
    {
        var a = new Mark(10, 2, 5);
        var b = new Mark(10, 3, 5);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentColumn_ReturnsFalse()
    {
        var a = new Mark(10, 2, 5);
        var b = new Mark(10, 2, 6);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_BoxedObject_ReturnsTrue()
    {
        var a = new Mark(10, 2, 5);
        object b = new Mark(10, 2, 5);

        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_BoxedDifferentType_ReturnsFalse()
    {
        var a = new Mark(10, 2, 5);

        Assert.False(a.Equals("not a mark"));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var a = new Mark(10, 2, 5);
        var b = new Mark(10, 2, 5);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentValues_LikelyDifferentHash()
    {
        var a = new Mark(10, 2, 5);
        var b = new Mark(11, 3, 6);

        // Hash codes are not guaranteed to differ, but for these inputs they should
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        var mark = new Mark(0, 1, 5);

        Assert.Equal("(Line: 1, Col: 5)", mark.ToString());
    }

    [Fact]
    public void ToString_LargeValues_FormatsCorrectly()
    {
        var mark = new Mark(99999, 1000, 250);

        Assert.Equal("(Line: 1000, Col: 250)", mark.ToString());
    }

    [Fact]
    public void CanBeUsedAsDictionaryKey()
    {
        var dict = new Dictionary<Mark, string>
        {
            [new Mark(0, 1, 1)] = "start",
            [new Mark(50, 3, 10)] = "middle",
        };

        Assert.Equal("start", dict[new Mark(0, 1, 1)]);
        Assert.Equal("middle", dict[new Mark(50, 3, 10)]);
    }
}
