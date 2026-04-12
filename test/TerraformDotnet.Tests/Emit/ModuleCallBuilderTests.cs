using System.Text;
using TerraformDotnet.Emit;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Emit;

public class ModuleCallBuilderTests
{
    private static TerraformModule ParseModule(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    private static TerraformModule SampleModule() => ParseModule("""
        variable "project" {
          type        = string
          description = "Project name."
        }

        variable "region" {
          type        = string
          description = "Deployment region."
        }

        variable "labels" {
          type        = map(string)
          description = "Resource labels."
        }

        variable "tier" {
          type        = string
          description = "The service tier."
          default     = "standard"
        }

        variable "max_count" {
          type        = number
          description = "Maximum instance count."
          default     = 5
        }

        variable "enable_ha" {
          type        = bool
          description = "Enable high availability."
          default     = false
        }
        """);

    [Fact]
    public void BasicBuild_WithModule()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("my-app", module)
            .Source("git::https://example.com/modules/app?ref=v1.0")
            .FillRequired(name => $"var.{name}")
            .Build();

        Assert.Equal("my-app", call.Name);
        Assert.Equal("git::https://example.com/modules/app?ref=v1.0", call.Source);
        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("var.project", call.Arguments["project"]);
        Assert.Equal("var.region", call.Arguments["region"]);
        Assert.Equal("var.labels", call.Arguments["labels"]);
    }

    [Fact]
    public void Set_RawExpression()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .Set("project", "var.project")
            .Set("region", "var.region")
            .Set("labels", "var.labels")
            .Build();

        Assert.Equal("var.project", call.Arguments["project"]);
    }

    [Fact]
    public void SetLiteral_String()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .SetLiteral("tier", "premium")
            .Build();

        Assert.Equal("\"premium\"", call.Arguments["tier"]);
    }

    [Fact]
    public void SetLiteral_Int()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .SetLiteral("max_count", 10)
            .Build();

        Assert.Equal("10", call.Arguments["max_count"]);
    }

    [Fact]
    public void SetLiteral_Bool()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .SetLiteral("enable_ha", true)
            .Build();

        Assert.Equal("true", call.Arguments["enable_ha"]);
    }

    [Fact]
    public void FillRequired_SkipsAlreadySetVariables()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .Set("project", "\"my-custom-project\"")
            .FillRequired(n => $"var.{n}")
            .Build();

        Assert.Equal("\"my-custom-project\"", call.Arguments["project"]);
        Assert.Equal("var.region", call.Arguments["region"]);
    }

    [Fact]
    public void BuildWithoutSource_Throws()
    {
        var builder = new ModuleCallBuilder("test");

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("source", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWithMissingRequired_Throws()
    {
        var module = SampleModule();

        var builder = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .Set("project", "var.project");
        // Missing "region" and "labels"

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("region", ex.Message);
        Assert.Contains("labels", ex.Message);
    }

    [Fact]
    public void DuplicateSet_OverwritesValue()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .Set("project", "var.old_project")
            .Set("project", "var.new_project")
            .Build();

        Assert.Equal("var.new_project", call.Arguments["project"]);
    }

    [Fact]
    public void IncludeOptionalComments_GeneratesCommentedVariables()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .SetLiteral("tier", "premium")
            .IncludeOptionalComments(true)
            .Build();

        // tier was explicitly set, so only max_count and enable_ha should be commented
        Assert.Equal(2, call.CommentedOptionalVariables.Count);
        Assert.Contains(call.CommentedOptionalVariables, c => c.Name == "max_count");
        Assert.Contains(call.CommentedOptionalVariables, c => c.Name == "enable_ha");

        var maxCount = call.CommentedOptionalVariables.First(c => c.Name == "max_count");
        Assert.Equal("Maximum instance count.", maxCount.Description);
        Assert.Equal("var.max_count", maxCount.SuggestedExpression);
    }

    [Fact]
    public void IncludeOptionalComments_False_NoComments()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .IncludeOptionalComments(false)
            .Build();

        Assert.Empty(call.CommentedOptionalVariables);
    }

    [Fact]
    public void MetaArguments_Count()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Count("2")
            .Build();

        Assert.Equal("2", call.Count);
    }

    [Fact]
    public void MetaArguments_ForEach()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .ForEach("var.instances")
            .Build();

        Assert.Equal("var.instances", call.ForEach);
    }

    [Fact]
    public void MetaArguments_DependsOn()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .DependsOn("module.network", "module.security")
            .Build();

        Assert.NotNull(call.DependsOn);
        Assert.Equal(2, call.DependsOn.Count);
        Assert.Equal("module.network", call.DependsOn[0]);
    }

    [Fact]
    public void MetaArguments_Providers()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Providers(new Dictionary<string, string>
            {
                ["cloud"] = "cloud.west",
            })
            .Build();

        Assert.NotNull(call.Providers);
        Assert.Equal("cloud.west", call.Providers["cloud"]);
    }

    [Fact]
    public void BuilderWithoutModule_NoValidation()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("anything", "any_value")
            .Build();

        Assert.Equal("any_value", call.Arguments["anything"]);
        Assert.Null(call.SourceModule);
    }

    [Fact]
    public void BuilderWithModule_StoresSourceModule()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .Build();

        Assert.Same(module, call.SourceModule);
    }

    [Fact]
    public void Version_SetsVersionOnCall()
    {
        var call = new ModuleCallBuilder("test")
            .Source("registry.example.com/example/test")
            .Version("~> 1.0")
            .Build();

        Assert.Equal("~> 1.0", call.Version);
    }

    [Fact]
    public void FillRequired_WithoutModule_Throws()
    {
        var builder = new ModuleCallBuilder("test")
            .Source("./modules/test");

        Assert.Throws<InvalidOperationException>(() =>
            builder.FillRequired(n => $"var.{n}"));
    }

    // ── FillSentinel ────────────────────────────────────────────

    [Fact]
    public void FillSentinel_AddsSentinelVariablesToArguments()
    {
        var module = SentinelModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .FillSentinel("pipeline-injected", n => $"var.{n}")
            .Build();

        Assert.Equal("var.project", call.Arguments["project"]);
        Assert.Equal("var.api_key", call.Arguments["api_key"]);
        Assert.Equal("var.secret", call.Arguments["secret"]);
        Assert.Equal(5, call.Arguments.Count);
    }

    [Fact]
    public void FillSentinel_SkipsAlreadySetVariables()
    {
        var module = SentinelModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .SetLiteral("api_key", "custom-value")
            .FillSentinel("pipeline-injected", n => $"var.{n}")
            .Build();

        Assert.Equal("\"custom-value\"", call.Arguments["api_key"]);
        Assert.Equal("var.secret", call.Arguments["secret"]);
    }

    [Fact]
    public void FillSentinel_DoesNotAffectNonSentinelOptionals()
    {
        var module = SentinelModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .FillSentinel("pipeline-injected", n => $"var.{n}")
            .Build();

        // "tier" has default "standard" → not a sentinel, so not in arguments
        Assert.False(call.Arguments.ContainsKey("tier"));
    }

    [Fact]
    public void FillSentinel_TracksCommentedInputVariables()
    {
        var module = SentinelModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .FillSentinel("pipeline-injected", n => $"var.{n}")
            .Build();

        Assert.Equal(2, call.CommentedInputVariables.Count);
        Assert.Contains(call.CommentedInputVariables, c => c.Name == "api_key");
        Assert.Contains(call.CommentedInputVariables, c => c.Name == "secret");

        var apiKey = call.CommentedInputVariables.First(c => c.Name == "api_key");
        Assert.Equal("The API key.", apiKey.Description);
        Assert.Equal("\"\"", apiKey.SuggestedExpression);
    }

    [Fact]
    public void FillSentinel_ExcludesSentinelFromCommentedOptionals()
    {
        var module = SentinelModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .FillSentinel("pipeline-injected", n => $"var.{n}")
            .IncludeOptionalComments(true)
            .Build();

        // Sentinel vars are in arguments, so they shouldn't be in commented optionals
        Assert.DoesNotContain(call.CommentedOptionalVariables, c => c.Name == "api_key");
        Assert.DoesNotContain(call.CommentedOptionalVariables, c => c.Name == "secret");
        // Only "tier" should be in commented optionals
        Assert.Single(call.CommentedOptionalVariables);
        Assert.Equal("tier", call.CommentedOptionalVariables[0].Name);
    }

    [Fact]
    public void FillSentinel_WithoutModule_Throws()
    {
        var builder = new ModuleCallBuilder("test")
            .Source("./modules/test");

        Assert.Throws<InvalidOperationException>(() =>
            builder.FillSentinel("pipeline-injected", n => $"var.{n}"));
    }

    [Fact]
    public void FillSentinel_NoSentinelVars_NoEffect()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("test", module)
            .Source("./modules/test")
            .FillRequired(n => $"var.{n}")
            .FillSentinel("pipeline-injected", n => $"var.{n}")
            .Build();

        // Only the 3 required vars should be set
        Assert.Equal(3, call.Arguments.Count);
        Assert.Empty(call.CommentedInputVariables);
    }

    private static TerraformModule SentinelModule() => ParseModule("""
        variable "project" {
          type        = string
          description = "Project name."
        }

        variable "region" {
          type        = string
          description = "Deployment region."
        }

        variable "labels" {
          type        = map(string)
          description = "Resource labels."
        }

        variable "api_key" {
          type        = string
          description = "The API key."
          default     = "pipeline-injected"
        }

        variable "secret" {
          type        = string
          description = "The secret."
          default     = "pipeline-injected"
        }

        variable "tier" {
          type        = string
          description = "The service tier."
          default     = "standard"
        }
        """);

    // ── Set(HclExpression) ──────────────────────────────────────

    [Fact]
    public void Set_HclExpression_VariableReference()
    {
        var expr = new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "var" },
            Name = "project",
        };

        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("project", expr)
            .Build();

        Assert.Equal("var.project", call.Arguments["project"]);
    }

    [Fact]
    public void Set_HclExpression_FunctionCall()
    {
        var expr = new HclFunctionCallExpression { Name = "merge" };
        expr.Arguments.Add(new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "var" },
            Name = "base_tags",
        });
        expr.Arguments.Add(new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "local" },
            Name = "extra_tags",
        });

        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("tags", expr)
            .Build();

        Assert.Equal("merge(var.base_tags, local.extra_tags)", call.Arguments["tags"]);
    }

    [Fact]
    public void Set_HclExpression_EmptyTuple()
    {
        var expr = new HclTupleExpression();

        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("items", expr)
            .Build();

        Assert.Equal("[]", call.Arguments["items"]);
    }

    [Fact]
    public void Set_HclExpression_OverridesPrevious()
    {
        var exprA = new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "var" },
            Name = "a",
        };

        var exprB = new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "var" },
            Name = "b",
        };

        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("field", exprA)
            .Set("field", exprB)
            .Build();

        Assert.Equal("var.b", call.Arguments["field"]);
    }

    [Fact]
    public void Set_HclExpression_MixedWithStringOverload()
    {
        var expr = new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "local" },
            Name = "computed",
        };

        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("a", "var.a")
            .Set("b", expr)
            .SetLiteral("c", "literal")
            .Build();

        Assert.Equal("var.a", call.Arguments["a"]);
        Assert.Equal("local.computed", call.Arguments["b"]);
        Assert.Equal("\"literal\"", call.Arguments["c"]);
    }
}
