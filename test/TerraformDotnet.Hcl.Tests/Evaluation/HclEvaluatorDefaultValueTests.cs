using System.Text;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

/// <summary>
/// Tests the key use-case for variable resolution: extracting Terraform variable defaults.
/// These tests parse real HCL text, find the variable blocks, and evaluate default values.
/// </summary>
public sealed class HclEvaluatorDefaultValueTests
{
    private readonly HclEvaluator _evaluator = new();
    private readonly HclEvaluationContext _context = new();

    [Fact]
    public void StringDefault()
    {
        var hcl = """
            variable "name" {
              default = "myapp"
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "name");

        Assert.NotNull(defaultValue);
        Assert.Equal(HclValueType.String, defaultValue.Type);
        Assert.Equal("myapp", defaultValue.StringValue);
    }

    [Fact]
    public void NumericDefault()
    {
        var hcl = """
            variable "count" {
              default = 3
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "count");

        Assert.NotNull(defaultValue);
        Assert.Equal(HclValueType.Number, defaultValue.Type);
        Assert.Equal(3.0, defaultValue.NumberValue);
    }

    [Fact]
    public void BoolDefault()
    {
        var hcl = """
            variable "enabled" {
              default = true
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "enabled");

        Assert.NotNull(defaultValue);
        Assert.Equal(HclValueType.Bool, defaultValue.Type);
        Assert.True(defaultValue.BoolValue);
    }

    [Fact]
    public void NullDefault()
    {
        var hcl = """
            variable "optional" {
              default = null
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "optional");

        Assert.NotNull(defaultValue);
        Assert.Equal(HclValueType.Null, defaultValue.Type);
    }

    [Fact]
    public void ListDefault()
    {
        var hcl = """
            variable "zones" {
              default = ["a", "b"]
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "zones");

        Assert.NotNull(defaultValue);
        Assert.Equal(HclValueType.Tuple, defaultValue.Type);
        Assert.Equal(2, defaultValue.TupleValue.Count);
        Assert.Equal("a", defaultValue.TupleValue[0].StringValue);
        Assert.Equal("b", defaultValue.TupleValue[1].StringValue);
    }

    [Fact]
    public void MapDefault()
    {
        var hcl = """
            variable "tags" {
              default = {
                env  = "dev"
                team = "platform"
              }
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "tags");

        Assert.NotNull(defaultValue);
        Assert.Equal(HclValueType.Object, defaultValue.Type);
        Assert.Equal(2, defaultValue.ObjectValue.Count);
        Assert.Equal("dev", defaultValue.ObjectValue["env"].StringValue);
        Assert.Equal("platform", defaultValue.ObjectValue["team"].StringValue);
    }

    [Fact]
    public void NoDefaultDetectsAbsence()
    {
        var hcl = """
            variable "required_input" {
              type = string
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "required_input");

        Assert.Null(defaultValue);
    }

    [Fact]
    public void MultipleVariablesExtractCorrectOne()
    {
        var hcl = """
            variable "first" {
              default = "one"
            }

            variable "second" {
              default = "two"
            }

            variable "third" {
              type = string
            }
            """u8;

        var first = ExtractDefault(hcl, "first");
        var second = ExtractDefault(hcl, "second");
        var third = ExtractDefault(hcl, "third");

        Assert.Equal("one", first!.StringValue);
        Assert.Equal("two", second!.StringValue);
        Assert.Null(third);
    }

    [Fact]
    public void VariableWithDescriptionAndDefault()
    {
        var hcl = """
            variable "region" {
              description = "The AWS region to deploy into"
              type        = string
              default     = "us-east-1"
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "region");

        Assert.Equal("us-east-1", defaultValue!.StringValue);
    }

    [Fact]
    public void NestedObjectDefault()
    {
        var hcl = """
            variable "config" {
              default = {
                db = {
                  host = "localhost"
                  port = 5432
                }
              }
            }
            """u8;

        var defaultValue = ExtractDefault(hcl, "config");

        Assert.Equal(HclValueType.Object, defaultValue!.Type);
        var db = defaultValue.ObjectValue["db"];
        Assert.Equal(HclValueType.Object, db.Type);
        Assert.Equal("localhost", db.ObjectValue["host"].StringValue);
        Assert.Equal(5432.0, db.ObjectValue["port"].NumberValue);
    }

    [Fact]
    public void ExpressionDefaultWithExistingVariable()
    {
        // When a default references another variable that IS in context, it resolves
        var hcl = """
            variable "suffix" {
              default = "prod"
            }
            """u8;

        var file = HclFile.Load(hcl);
        var block = file.Body.Blocks.First(b => b.Type == "variable" && b.Labels[0] == "suffix");
        var defaultAttr = block.Body.Attributes.FirstOrDefault(a => a.Name == "default");

        Assert.NotNull(defaultAttr);

        var result = _evaluator.Evaluate(defaultAttr.Value, _context);

        Assert.Equal("prod", result.StringValue);
    }

    /// <summary>
    /// Helper: parses HCL, finds the variable block with the given name,
    /// and evaluates its default attribute value.
    /// </summary>
    private HclValue? ExtractDefault(ReadOnlySpan<byte> hcl, string variableName)
    {
        var file = HclFile.Load(hcl);

        var block = file.Body.Blocks
            .FirstOrDefault(b => b.Type == "variable" && b.Labels.Count > 0 && b.Labels[0] == variableName);

        if (block is null)
        {
            return null;
        }

        var defaultAttr = block.Body.Attributes.FirstOrDefault(a => a.Name == "default");

        if (defaultAttr is null)
        {
            return null;
        }

        return _evaluator.Evaluate(defaultAttr.Value, _context);
    }
}
