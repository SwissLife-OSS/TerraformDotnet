using System.Text;
using TerraformDotnet.Emit;
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
}
