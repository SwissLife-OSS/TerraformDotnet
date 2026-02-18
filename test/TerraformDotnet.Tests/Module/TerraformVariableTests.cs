using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;
using TerraformDotnet.Types;

namespace TerraformDotnet.Tests.Module;

public class TerraformVariableTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void RequiredStringVariable()
    {
        var module = Parse("""
            variable "project_name" {
              type        = string
              description = "The name of the project."
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.Equal("project_name", v.Name);
        Assert.Equal(TerraformType.String, v.Type);
        Assert.Equal("The name of the project.", v.Description);
        Assert.True(v.IsRequired);
        Assert.False(v.IsOptional);
        Assert.Null(v.Default);
    }

    [Fact]
    public void OptionalStringVariableWithDefault()
    {
        var module = Parse("""
            variable "tier" {
              type        = string
              description = "The tier level."
              default     = "standard"
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.Equal("tier", v.Name);
        Assert.False(v.IsRequired);
        Assert.True(v.IsOptional);
        Assert.NotNull(v.Default);
        var lit = Assert.IsType<HclLiteralExpression>(v.Default);
        Assert.Equal("standard", lit.Value);
    }

    [Fact]
    public void OptionalBoolVariableWithDefault()
    {
        var module = Parse("""
            variable "enable_logging" {
              type    = bool
              default = true
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.Equal(TerraformType.Bool, v.Type);
        Assert.True(v.IsOptional);
        var lit = Assert.IsType<HclLiteralExpression>(v.Default);
        Assert.Equal("true", lit.Value);
    }

    [Fact]
    public void OptionalNumberVariable()
    {
        var module = Parse("""
            variable "count" {
              type    = number
              default = 1
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.Equal(TerraformType.Number, v.Type);
        Assert.True(v.IsOptional);
    }

    [Fact]
    public void VariableWithDescription()
    {
        var module = Parse("""
            variable "region" {
              type        = string
              description = "The deployment region."
            }
            """);

        Assert.Equal("The deployment region.", module.Variables[0].Description);
    }

    [Fact]
    public void SensitiveVariable()
    {
        var module = Parse("""
            variable "secret" {
              type      = string
              sensitive = true
            }
            """);

        Assert.True(module.Variables[0].IsSensitive);
    }

    [Fact]
    public void NullableVariable()
    {
        var module = Parse("""
            variable "nullable_field" {
              type     = string
              default  = null
              nullable = true
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.True(v.IsNullable);
        Assert.True(v.IsOptional);
    }

    [Fact]
    public void VariableWithValidationBlock()
    {
        var module = Parse("""
            variable "retention" {
              type    = number
              default = 7

              validation {
                condition     = var.retention >= 1 && var.retention <= 35
                error_message = "Must be between 1 and 35."
              }
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.NotNull(v.Validation);
        Assert.Equal("Must be between 1 and 35.", v.Validation.ErrorMessage);
        Assert.NotNull(v.Validation.Condition);
    }

    [Fact]
    public void VariableWithComplexObjectType()
    {
        var module = Parse("""
            variable "alert_config" {
              type = object({
                enabled   = optional(bool, true)
                severity  = optional(number, 3)
                threshold = optional(number, 80)
              })
              default = {}
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.IsType<TerraformObjectType>(v.Type);
        var obj = (TerraformObjectType)v.Type!;
        Assert.Equal(3, obj.Fields.Count);
        Assert.All(obj.Fields, f => Assert.True(f.IsOptional));
    }

    [Fact]
    public void VariableWithListStringDefault()
    {
        var module = Parse("""
            variable "cidrs" {
              type    = list(string)
              default = []
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.Equal(TerraformType.List(TerraformType.String), v.Type);
        Assert.True(v.IsOptional);
        Assert.IsType<HclTupleExpression>(v.Default);
    }

    [Fact]
    public void VariableWithNoType()
    {
        var module = Parse("""
            variable "anything" {
              description = "No type specified."
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.Null(v.Type);
        Assert.True(v.IsRequired);
    }

    [Fact]
    public void VariableWithMapStringType()
    {
        var module = Parse("""
            variable "labels" {
              type = map(string)
            }
            """);

        var v = Assert.Single(module.Variables);
        Assert.Equal(TerraformType.Map(TerraformType.String), v.Type);
    }

    [Fact]
    public void NonSensitiveVariableDefaultsFalse()
    {
        var module = Parse("""
            variable "name" {
              type = string
            }
            """);

        Assert.False(module.Variables[0].IsSensitive);
    }

    [Fact]
    public void NonNullableVariableDefaultsFalse()
    {
        var module = Parse("""
            variable "name" {
              type = string
            }
            """);

        Assert.False(module.Variables[0].IsNullable);
    }

    [Fact]
    public void VariableWithSetOfObjectType()
    {
        var module = Parse("""
            variable "configs" {
              type = set(object({
                name     = string
                region   = string
                priority = optional(number, 10)
              }))
              default = []
            }
            """);

        var v = Assert.Single(module.Variables);
        var collType = Assert.IsType<TerraformCollectionType>(v.Type);
        Assert.Equal(TerraformTypeKind.Set, collType.Kind);
        var innerObj = Assert.IsType<TerraformObjectType>(collType.Element);
        Assert.Equal(3, innerObj.Fields.Count);
    }
}
