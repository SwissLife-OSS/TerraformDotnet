using System.Text;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Nodes;

public sealed class HclFileParserRegressionTests
{
    private static HclFile Load(string hcl) => HclFile.Load(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void AttributeAfterTopLevelHeredocIsParsed()
    {
        var file = Load("x = <<DESC\nhello\nDESC\ny = 1\n");

        Assert.Equal(["x", "y"], file.Body.Attributes.Select(a => a.Name));
    }

    [Fact]
    public void AttributeAfterHeredocInsideBlockIsParsed()
    {
        var file = Load("""
            variable "a" {
              description = <<-DESC
                Some text
                  indented
                DESC
              type        = string
              default     = "x"
            }
            """);

        var body = Assert.Single(file.Body.Blocks).Body;
        Assert.Equal(["description", "type", "default"], body.Attributes.Select(a => a.Name));
    }

    [Fact]
    public void HeredocContentIsPreservedWhenFollowedByAttribute()
    {
        var file = Load("x = <<DESC\nhello\nworld\nDESC\ny = 1\n");

        var literal = Assert.IsType<HclLiteralExpression>(file.Body.Attributes[0].Value);
        Assert.Equal("hello\nworld\n", literal.Value);
    }

    [Fact]
    public void MultipleHeredocsInARowAreParsed()
    {
        var file = Load("a = <<ONE\n1\nONE\nb = <<TWO\n2\nTWO\nc = 3\n");

        Assert.Equal(["a", "b", "c"], file.Body.Attributes.Select(a => a.Name));
    }

    [Fact]
    public void HeredocWithWindowsLineEndingsFollowedByAttributeIsParsed()
    {
        var file = Load("x = <<DESC\r\nhello\r\nDESC\r\ny = 1\r\n");

        Assert.Equal(["x", "y"], file.Body.Attributes.Select(a => a.Name));
    }

    [Fact]
    public void ProviderFunctionCallIsParsed()
    {
        var file = Load("x = provider::azapi::parse_resource_id(\"a\", var.b)\n");

        var call = Assert.IsType<HclFunctionCallExpression>(file.Body.Attributes[0].Value);
        Assert.Equal("provider::azapi::parse_resource_id", call.Name);
        Assert.Equal(2, call.Arguments.Count);
    }

    [Fact]
    public void ProviderFunctionResultCanBeTraversed()
    {
        var expression = HclExpression.Parse("provider::azapi::parse_resource_id(\"t\", var.id).name != \"\"");

        var binary = Assert.IsType<HclBinaryExpression>(expression);
        var access = Assert.IsType<HclAttributeAccessExpression>(binary.Left);
        Assert.IsType<HclFunctionCallExpression>(access.Source);
    }

    [Fact]
    public void ProviderFunctionInsideValidationBlockIsParsed()
    {
        var file = Load("""
            validation {
              condition     = provider::azapi::is_valid(var.x)
              error_message = "bad"
            }
            """);

        Assert.Equal(["condition", "error_message"], Assert.Single(file.Body.Blocks).Body.Attributes.Select(a => a.Name));
    }

    [Fact]
    public void TernaryWithSingleColonIsUnaffected()
    {
        var expression = HclExpression.Parse("var.a ? b :c");

        Assert.IsType<HclConditionalExpression>(expression);
    }
}
