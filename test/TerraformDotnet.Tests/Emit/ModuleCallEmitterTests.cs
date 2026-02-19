using System.Text;
using TerraformDotnet.Emit;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Emit;

public class ModuleCallEmitterTests
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

    // ── EmitModuleBlock ─────────────────────────────────────────

    [Fact]
    public void EmitModuleBlock_MinimalCall()
    {
        var call = new ModuleCallBuilder("cache")
            .Source("./modules/cache")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        Assert.Contains("module \"cache\" {", result);
        Assert.Contains("source = \"./modules/cache\"", result);
        Assert.EndsWith("}", result);
    }

    [Fact]
    public void EmitModuleBlock_SourceAndVersion()
    {
        var call = new ModuleCallBuilder("registry")
            .Source("registry.example.com/org/network")
            .Version("~> 2.0")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        Assert.Contains("source  = \"registry.example.com/org/network\"", result);
        Assert.Contains("version = \"~> 2.0\"", result);
    }

    [Fact]
    public void EmitModuleBlock_WithArguments_Aligned()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        Assert.Contains("source = \"./modules/app\"", result);
        Assert.Contains("project = var.project", result);
        Assert.Contains("region  = var.region", result);
        Assert.Contains("labels  = var.labels", result);
    }

    [Fact]
    public void EmitModuleBlock_WithCommentedOptionals()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .IncludeOptionalComments(true)
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        Assert.Contains("# (Optional) The service tier.", result);
        Assert.Contains("# tier", result);
        Assert.Contains("# (Optional) Maximum instance count.", result);
        Assert.Contains("# max_count", result);
        Assert.Contains("# (Optional) Enable high availability.", result);
        Assert.Contains("# enable_ha", result);
    }

    [Fact]
    public void EmitModuleBlock_WithMetaArguments()
    {
        var call = new ModuleCallBuilder("worker")
            .Source("./modules/worker")
            .Set("name", "var.worker_name")
            .Count("var.worker_count")
            .DependsOn("module.network", "module.security")
            .Providers(new Dictionary<string, string>
            {
                ["cloud"] = "cloud.west",
            })
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        Assert.Contains("count      = var.worker_count", result);
        Assert.Contains("depends_on = [module.network, module.security]", result);
        Assert.Contains("providers  = { cloud = cloud.west }", result);
    }

    [Fact]
    public void EmitModuleBlock_GroupsAlignedIndependently()
    {
        var call = new ModuleCallBuilder("svc")
            .Source("git::https://example.com/modules/svc?ref=v1.0")
            .Set("department", "var.department")
            .Set("environment", "var.environment")
            .Set("tags", "local.tags")
            .Count("var.svc_count")
            .DependsOn("module.network")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        // Source group: only "source" → no padding
        Assert.Contains("source = \"git::https://example.com/modules/svc?ref=v1.0\"", result);

        // Args group: aligned to "environment" (11 chars)
        Assert.Contains("department  = var.department", result);
        Assert.Contains("environment = var.environment", result);
        Assert.Contains("tags        = local.tags", result);

        // Meta group: aligned to "depends_on" (10 chars)
        Assert.Contains("count      = var.svc_count", result);
        Assert.Contains("depends_on = [module.network]", result);
    }

    [Fact]
    public void EmitModuleBlock_WithForEach()
    {
        var call = new ModuleCallBuilder("instance")
            .Source("./modules/instance")
            .Set("id", "each.key")
            .ForEach("var.instances")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        Assert.Contains("for_each = var.instances", result);
    }

    [Fact]
    public void EmitModuleBlock_BlankLineBetweenSourceAndArguments()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("project", "var.project")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        var lines = result.Split('\n', StringSplitOptions.None);
        var sourceIndex = Array.FindIndex(lines, l => l.Contains("source"));
        var projectIndex = Array.FindIndex(lines, l => l.Contains("project"));

        // There should be a blank line between source and arguments
        Assert.True(projectIndex > sourceIndex + 1,
            "Expected blank line between source and arguments");
        Assert.Equal("", lines[sourceIndex + 1].Trim());
    }

    // ── EmitVariableDeclarations ────────────────────────────────

    [Fact]
    public void EmitVariableDeclarations_Minimal()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitVariableDeclarations();

        Assert.Contains("variable \"project\" {}", result);
        Assert.Contains("variable \"region\" {}", result);
        Assert.Contains("variable \"labels\" {}", result);
    }

    [Fact]
    public void EmitVariableDeclarations_WithType()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var options = new VariableDeclarationOptions { IncludeType = true };
        var result = emitter.EmitVariableDeclarations(options);

        Assert.Contains("type = string", result);
        Assert.Contains("type = map(string)", result);
    }

    [Fact]
    public void EmitVariableDeclarations_WithDescription()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var options = new VariableDeclarationOptions { IncludeDescription = true };
        var result = emitter.EmitVariableDeclarations(options);

        Assert.Contains("description = \"Project name.\"", result);
        Assert.Contains("description = \"Deployment region.\"", result);
    }

    [Fact]
    public void EmitVariableDeclarations_WithAllOptions()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .SetLiteral("tier", "premium")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var options = new VariableDeclarationOptions
        {
            IncludeType = true,
            IncludeDescription = true,
            IncludeDefault = true,
        };
        var result = emitter.EmitVariableDeclarations(options);

        // Required variables have type and description but no default
        Assert.Contains("variable \"project\" {", result);
        Assert.Contains("type = string", result);
        Assert.Contains("description = \"Project name.\"", result);

        // tier has a default in the module
        Assert.Contains("variable \"tier\" {", result);
        Assert.Contains("description = \"The service tier.\"", result);
    }

    [Fact]
    public void EmitVariableDeclarations_WithoutModule()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("anything", "var.anything")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitVariableDeclarations();

        Assert.Contains("variable \"anything\" {}", result);
    }

    [Fact]
    public void EmitVariableDeclarations_CompactSpacing_NoBlankLinesBetweenBlocks()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var options = new VariableDeclarationOptions { CompactSpacing = true };
        var result = emitter.EmitVariableDeclarations(options);

        // Should not contain double newlines (blank lines between blocks)
        Assert.DoesNotContain("\n\n", result);
        Assert.Contains("variable \"project\" {}", result);
        Assert.Contains("variable \"region\" {}", result);
    }

    // ── EmitInputValues ─────────────────────────────────────────

    [Fact]
    public void EmitInputValues_SimpleValues()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("name", "var.name")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["name"] = "\"my-project\"",
            ["region"] = "\"us-west-2\"",
        };

        var result = emitter.EmitInputValues(values);

        Assert.Contains("name   = \"my-project\"", result);
        Assert.Contains("region = \"us-west-2\"", result);
    }

    [Fact]
    public void EmitInputValues_WithInlineComment()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["project"] = new InputValue("\"my-app\"", "JIRA-1234"),
        };

        var result = emitter.EmitInputValues(values);

        Assert.Contains("\"my-app\" # JIRA-1234", result);
    }

    [Fact]
    public void EmitInputValues_WithModuleDescriptions()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["project"] = "\"my-app\"",
            ["region"] = "\"eu-west-1\"",
        };

        var result = emitter.EmitInputValues(values);

        Assert.Contains("# (Required) Project name.", result);
        Assert.Contains("# (Required) Deployment region.", result);
    }

    [Fact]
    public void EmitInputValues_OptionalDescriptionLabel()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["tier"] = "\"premium\"",
        };

        var result = emitter.EmitInputValues(values);

        Assert.Contains("# (Optional) The service tier.", result);
    }

    [Fact]
    public void EmitInputValues_AlignedKeys()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./t")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["x"] = "1",
            ["long_name"] = "2",
        };

        var result = emitter.EmitInputValues(values);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Both '=' signs should be at the same column
        var eqPositions = lines
            .Where(l => l.Contains('='))
            .Select(l => l.IndexOf('='))
            .Distinct()
            .ToList();

        Assert.Single(eqPositions);
    }

    [Fact]
    public void EmitInputValues_CompactSpacing_NoBlankLinesBetweenValues()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Set("name", "var.name")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["name"] = "\"my-project\"",
            ["region"] = "\"us-west-2\"",
            ["tier"] = "\"basic\"",
        };

        var result = emitter.EmitInputValues(values, compactSpacing: true);
        var lines = result.Split('\n');

        // No blank lines between assignments
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].Contains('='))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(lines[i + 1]) && i + 2 < lines.Length && lines[i + 2].Contains('='),
                    "Compact spacing should not have blank lines between values");
            }
        }

        Assert.Contains("name", result);
        Assert.Contains("region", result);
        Assert.Contains("tier", result);
    }

    [Fact]
    public void EmitInputValues_DefaultSpacing_HasBlankLinesBetweenValues()
    {
        var call = new ModuleCallBuilder("test")
            .Source("./modules/test")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["name"] = "\"a\"",
            ["region"] = "\"b\"",
        };

        var result = emitter.EmitInputValues(values);

        // Default has a blank line between entries
        Assert.Contains("\"a\"\n\nregion", result);
    }

    // ── WriteTo ─────────────────────────────────────────────────

    [Fact]
    public void WriteTo_CreatesModuleFile()
    {
        var call = new ModuleCallBuilder("db")
            .Source("./modules/db")
            .Set("name", "var.name")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            emitter.WriteTo(tempDir, new FileEmitterOptions());

            var moduleFile = Path.Combine(tempDir, "resources-db.tf");
            Assert.True(File.Exists(moduleFile));
            var content = File.ReadAllText(moduleFile);
            Assert.Contains("module \"db\"", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteTo_CustomModuleFileName()
    {
        var call = new ModuleCallBuilder("db")
            .Source("./modules/db")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            emitter.WriteTo(tempDir, new FileEmitterOptions
            {
                ModuleFileName = "custom-module.tf",
            });

            Assert.True(File.Exists(Path.Combine(tempDir, "custom-module.tf")));
            Assert.False(File.Exists(Path.Combine(tempDir, "resources-db.tf")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteTo_GeneratesVariablesFile()
    {
        var module = SampleModule();

        var call = new ModuleCallBuilder("app", module)
            .Source("./modules/app")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            emitter.WriteTo(tempDir, new FileEmitterOptions
            {
                VariablesFileName = "variables-app.tf",
            });

            var variablesFile = Path.Combine(tempDir, "variables-app.tf");
            Assert.True(File.Exists(variablesFile));
            var content = File.ReadAllText(variablesFile);
            Assert.Contains("variable \"project\"", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteTo_GeneratesInputFiles()
    {
        var call = new ModuleCallBuilder("app")
            .Source("./modules/app")
            .Set("project", "var.project")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var devValues = new Dictionary<string, InputValue>
            {
                ["project"] = "\"dev-project\"",
            };

            var prodValues = new Dictionary<string, InputValue>
            {
                ["project"] = "\"prod-project\"",
            };

            emitter.WriteTo(tempDir, new FileEmitterOptions
            {
                InputFiles = new Dictionary<string, IDictionary<string, InputValue>>
                {
                    ["input-dev.tfvars"] = devValues,
                    ["input-prod.tfvars"] = prodValues,
                },
            });

            var devFile = Path.Combine(tempDir, "input-dev.tfvars");
            var prodFile = Path.Combine(tempDir, "input-prod.tfvars");
            Assert.True(File.Exists(devFile));
            Assert.True(File.Exists(prodFile));
            Assert.Contains("dev-project", File.ReadAllText(devFile));
            Assert.Contains("prod-project", File.ReadAllText(prodFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ── End-to-end with __assets__ ──────────────────────────────

    [Fact]
    public void EmitModuleBlock_ComputeModuleAsset()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/compute-module");

        var call = new ModuleCallBuilder("compute", module)
            .Source("git::https://example.com/modules/compute?ref=v3.2")
            .Version("~> 3.0")
            .FillRequired(n => $"var.{n}")
            .IncludeOptionalComments(true)
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var result = emitter.EmitModuleBlock();

        Assert.Contains("module \"compute\"", result);
        Assert.Contains("source", result);
        Assert.Contains("version", result);
        Assert.Contains("var.project_name", result);
        Assert.Contains("var.region", result);
        Assert.Contains("var.owner", result);
        Assert.Contains("var.labels", result);

        // Optional variables should be commented
        Assert.Contains("# instance_count", result);
        Assert.Contains("# enable_logging", result);
    }

    [Fact]
    public void EmitVariableDeclarations_ComputeModuleAsset_WithAllOptions()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/compute-module");

        var call = new ModuleCallBuilder("compute", module)
            .Source("git::https://example.com/modules/compute?ref=v3.2")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var options = new VariableDeclarationOptions
        {
            IncludeType = true,
            IncludeDescription = true,
        };
        var result = emitter.EmitVariableDeclarations(options);

        Assert.Contains("variable \"project_name\"", result);
        Assert.Contains("type = string", result);
        Assert.Contains("description = \"The name of the project.\"", result);
        Assert.Contains("type = map(string)", result);
    }
}
