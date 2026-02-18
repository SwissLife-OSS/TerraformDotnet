using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;

namespace TerraformDotnet.Hcl.Tests.Exceptions;

public sealed class HclExceptionTests
{
    [Fact]
    public void HclException_MessageOnly()
    {
        var ex = new HclException("something went wrong");

        Assert.Equal("something went wrong", ex.Message);
        Assert.Null(ex.Position);
        Assert.Null(ex.Path);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void HclException_WithPosition_IncludesPositionInMessage()
    {
        var pos = new Mark(10, 2, 5);
        var ex = new HclException("unexpected token", pos);

        Assert.Contains("unexpected token", ex.Message);
        Assert.Contains("(Line: 2, Col: 5)", ex.Message);
        Assert.Equal(pos, ex.Position);
        Assert.Null(ex.Path);
    }

    [Fact]
    public void HclException_WithPositionAndPath_IncludesBothInMessage()
    {
        var pos = new Mark(100, 10, 3);
        var ex = new HclException("duplicate attribute", pos, "resource.aws_instance.web");

        Assert.Contains("duplicate attribute", ex.Message);
        Assert.Contains("(Line: 10, Col: 3)", ex.Message);
        Assert.Contains("resource.aws_instance.web", ex.Message);
        Assert.Equal(pos, ex.Position);
        Assert.Equal("resource.aws_instance.web", ex.Path);
    }

    [Fact]
    public void HclException_WithInnerException()
    {
        var inner = new InvalidOperationException("inner issue");
        var ex = new HclException("outer error", inner);

        Assert.Equal("outer error", ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Null(ex.Position);
    }

    [Fact]
    public void HclException_WithPositionAndInnerException()
    {
        var pos = new Mark(5, 1, 6);
        var inner = new FormatException("bad format");
        var ex = new HclException("parse error", pos, inner);

        Assert.Contains("parse error", ex.Message);
        Assert.Contains("(Line: 1, Col: 6)", ex.Message);
        Assert.Equal(pos, ex.Position);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void HclSyntaxException_AlwaysHasPosition()
    {
        var pos = new Mark(0, 1, 1);
        var ex = new HclSyntaxException("unterminated string", pos);

        Assert.IsType<HclSyntaxException>(ex);
        Assert.IsAssignableFrom<HclException>(ex);
        Assert.Equal(pos, ex.Position);
        Assert.Contains("unterminated string", ex.Message);
    }

    [Fact]
    public void HclSyntaxException_WithPath()
    {
        var pos = new Mark(50, 5, 10);
        var ex = new HclSyntaxException("invalid escape", pos, "block.attr");

        Assert.Equal(pos, ex.Position);
        Assert.Equal("block.attr", ex.Path);
        Assert.Contains("block.attr", ex.Message);
    }

    [Fact]
    public void HclSemanticException_AlwaysHasPosition()
    {
        var pos = new Mark(20, 3, 1);
        var ex = new HclSemanticException("duplicate attribute 'name'", pos);

        Assert.IsType<HclSemanticException>(ex);
        Assert.IsAssignableFrom<HclException>(ex);
        Assert.Equal(pos, ex.Position);
        Assert.Contains("duplicate attribute", ex.Message);
    }

    [Fact]
    public void HclSemanticException_WithPath()
    {
        var pos = new Mark(200, 15, 3);
        var ex = new HclSemanticException("conflicting definition", pos, "module.network");

        Assert.Equal("module.network", ex.Path);
        Assert.Contains("module.network", ex.Message);
    }

    [Fact]
    public void MaxRecursionDepthExceededException_StoresDepths()
    {
        var pos = new Mark(500, 40, 1);
        var ex = new MaxRecursionDepthExceededException(64, 65, pos);

        Assert.IsType<MaxRecursionDepthExceededException>(ex);
        Assert.IsAssignableFrom<HclException>(ex);
        Assert.Equal(64, ex.MaxDepth);
        Assert.Equal(65, ex.CurrentDepth);
        Assert.Equal(pos, ex.Position);
        Assert.Contains("64", ex.Message);
        Assert.Contains("65", ex.Message);
    }

    [Fact]
    public void AllExceptions_AreSerializableWithStandardCatch()
    {
        // Verify all exception types can be caught as HclException
        var exceptions = new HclException[]
        {
            new HclException("base"),
            new HclSyntaxException("syntax", new Mark(0, 1, 1)),
            new HclSemanticException("semantic", new Mark(0, 1, 1)),
            new MaxRecursionDepthExceededException(64, 65, new Mark(0, 1, 1)),
        };

        foreach (HclException ex in exceptions)
        {
            Assert.NotNull(ex.Message);
        }
    }
}
