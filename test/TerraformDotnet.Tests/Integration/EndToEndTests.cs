using System.Text;
using TerraformDotnet.Emit;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Integration;

public class EndToEndTests
{
    // ── ComputeModule: load → build → emit → parse-back ─────────

    [Fact]
    public void ComputeModule_RoundTrip_EmittedModuleBlockIsValidHcl()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/compute-module");

        var call = new ModuleCallBuilder("compute", module)
            .Source("git::https://example.com/modules/compute?ref=v3.2")
            .Version("~> 3.0")
            .FillRequired(n => $"var.{n}")
            .IncludeOptionalComments(true)
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var hcl = emitter.EmitModuleBlock();

        var parsed = HclFile.Load(Encoding.UTF8.GetBytes(hcl));

        Assert.Single(parsed.Body.Blocks);
        Assert.Equal("module", parsed.Body.Blocks[0].Type);
        Assert.Equal("compute", parsed.Body.Blocks[0].Labels[0]);
    }

    [Fact]
    public void ComputeModule_VariableDeclarations_ValidHcl()
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
        var hcl = emitter.EmitVariableDeclarations(options);

        var parsed = HclFile.Load(Encoding.UTF8.GetBytes(hcl));
        var varBlocks = parsed.Body.Blocks.Where(b => b.Type == "variable").ToList();

        Assert.Equal(call.Arguments.Count, varBlocks.Count);

        foreach (var block in varBlocks)
        {
            Assert.Single(block.Labels);
            Assert.Contains(call.Arguments.Keys, k => k == block.Labels[0]);
        }
    }

    [Fact]
    public void ComputeModule_InputValues_ValidHclAssignments()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/compute-module");

        var call = new ModuleCallBuilder("compute", module)
            .Source("git::https://example.com/modules/compute?ref=v3.2")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var values = new Dictionary<string, InputValue>
        {
            ["project_name"] = new InputValue("\"web-portal\"", "Primary web project"),
            ["region"] = "\"eu-north-1\"",
            ["owner"] = "\"platform-team\"",
            ["labels"] = "{}",
        };

        var result = emitter.EmitInputValues(values);

        Assert.Contains("project_name", result);
        Assert.Contains("\"web-portal\"", result);
        Assert.Contains("# Primary web project", result);
        Assert.Contains("# (Required) The name of the project.", result);
        Assert.Contains("# (Required) The deployment region.", result);
    }

    // ── DatabaseModule: load → inspect → build → emit ───────────

    [Fact]
    public void DatabaseModule_LoadAndInspect()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/database-module");

        Assert.True(module.RequiredVariables.Count > 0);
        Assert.True(module.OptionalVariables.Count > 0);
        Assert.True(module.Resources.Count > 0);
        Assert.True(module.Outputs.Count > 0);

        var sensitiveVars = module.Variables.Where(v => v.IsSensitive).ToList();
        Assert.True(sensitiveVars.Count > 0);

        var sensitiveOutputs = module.Outputs.Where(o => o.IsSensitive).ToList();
        Assert.True(sensitiveOutputs.Count > 0);
    }

    [Fact]
    public void DatabaseModule_BuildAndEmit()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/database-module");

        var call = new ModuleCallBuilder("database", module)
            .Source("./modules/database")
            .FillRequired(n => $"var.{n}")
            .IncludeOptionalComments(true)
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var hcl = emitter.EmitModuleBlock();

        var parsed = HclFile.Load(Encoding.UTF8.GetBytes(hcl));
        Assert.Single(parsed.Body.Blocks);
        Assert.Equal("database", parsed.Body.Blocks[0].Labels[0]);
    }

    // ── EmptyModule ─────────────────────────────────────────────

    [Fact]
    public void EmptyModule_LoadAndEmit()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/empty-module");

        Assert.Empty(module.Variables);
        Assert.Empty(module.Outputs);

        var call = new ModuleCallBuilder("empty", module)
            .Source("./modules/empty")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var hcl = emitter.EmitModuleBlock();

        Assert.Contains("module \"empty\"", hcl);
        Assert.Contains("source = \"./modules/empty\"", hcl);
    }

    // ── Multi-environment output ────────────────────────────────

    [Fact]
    public void MultiEnvironment_GeneratesDistinctInputFiles()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/compute-module");

        var call = new ModuleCallBuilder("compute", module)
            .Source("git::https://example.com/modules/compute?ref=v3.2")
            .FillRequired(n => $"var.{n}")
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var devValues = new Dictionary<string, InputValue>
            {
                ["project_name"] = "\"dev-portal\"",
                ["region"] = "\"eu-west-1\"",
                ["owner"] = "\"dev-team\"",
                ["labels"] = "{}",
            };

            var prodValues = new Dictionary<string, InputValue>
            {
                ["project_name"] = "\"prod-portal\"",
                ["region"] = "\"eu-west-1\"",
                ["owner"] = "\"sre-team\"",
                ["labels"] = "{ environment = \"production\" }",
            };

            emitter.WriteTo(tempDir, new FileEmitterOptions
            {
                ModuleFileName = "resources-compute.tf",
                VariablesFileName = "variables-compute.tf",
                InputFiles = new Dictionary<string, IDictionary<string, InputValue>>
                {
                    ["input-dev.tfvars"] = devValues,
                    ["input-prod.tfvars"] = prodValues,
                },
            });

            Assert.True(File.Exists(Path.Combine(tempDir, "resources-compute.tf")));
            Assert.True(File.Exists(Path.Combine(tempDir, "variables-compute.tf")));
            Assert.True(File.Exists(Path.Combine(tempDir, "input-dev.tfvars")));
            Assert.True(File.Exists(Path.Combine(tempDir, "input-prod.tfvars")));

            var devContent = File.ReadAllText(Path.Combine(tempDir, "input-dev.tfvars"));
            var prodContent = File.ReadAllText(Path.Combine(tempDir, "input-prod.tfvars"));

            Assert.Contains("\"dev-portal\"", devContent);
            Assert.Contains("\"prod-portal\"", prodContent);
            Assert.Contains("\"dev-team\"", devContent);
            Assert.Contains("\"sre-team\"", prodContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ── Full pipeline with meta-arguments ───────────────────────

    [Fact]
    public void FullPipeline_WithMetaArguments_ValidHcl()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/compute-module");

        var call = new ModuleCallBuilder("compute", module)
            .Source("registry.example.com/org/compute")
            .Version("~> 3.0")
            .FillRequired(n => $"var.{n}")
            .ForEach("var.environments")
            .DependsOn("module.network", "module.security")
            .Providers(new Dictionary<string, string>
            {
                ["cloud"] = "cloud.primary",
            })
            .IncludeOptionalComments(true)
            .Build();

        var emitter = new ModuleCallEmitter(call);
        var hcl = emitter.EmitModuleBlock();

        var parsed = HclFile.Load(Encoding.UTF8.GetBytes(hcl));
        var moduleBlock = parsed.Body.Blocks[0];

        // Verify source and version are in the body
        var attrs = moduleBlock.Body.Attributes.Select(a => a.Name).ToList();
        Assert.Contains("source", attrs);
        Assert.Contains("version", attrs);
        Assert.Contains("for_each", attrs);
        Assert.Contains("depends_on", attrs);
        Assert.Contains("providers", attrs);
    }

    // ── Inline module content round-trip ────────────────────────

    [Fact]
    public void InlineModule_FullRoundTrip()
    {
        var module = TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes("""
            variable "cluster_name" {
              type        = string
              description = "Name of the compute cluster."
            }

            variable "node_count" {
              type        = number
              description = "Number of worker nodes."
            }

            variable "enable_monitoring" {
              type        = bool
              description = "Enable cluster monitoring."
              default     = true
            }

            output "cluster_endpoint" {
              value       = "https://cluster.example.com"
              description = "The cluster API endpoint."
            }

            resource "cloud_cluster" "main" {
              name       = var.cluster_name
              node_count = var.node_count
            }
            """));

        Assert.Equal(2, module.RequiredVariables.Count);
        Assert.Single(module.OptionalVariables);
        Assert.Single(module.Outputs);
        Assert.Single(module.Resources);

        var call = new ModuleCallBuilder("cluster", module)
            .Source("./modules/cluster")
            .FillRequired(n => $"var.{n}")
            .IncludeOptionalComments(true)
            .Build();

        var emitter = new ModuleCallEmitter(call);

        var moduleHcl = emitter.EmitModuleBlock();
        var parsed = HclFile.Load(Encoding.UTF8.GetBytes(moduleHcl));
        Assert.Single(parsed.Body.Blocks);

        var varHcl = emitter.EmitVariableDeclarations(new VariableDeclarationOptions
        {
            IncludeType = true,
            IncludeDescription = true,
        });

        var parsedVars = HclFile.Load(Encoding.UTF8.GetBytes(varHcl));
        Assert.Equal(2, parsedVars.Body.Blocks.Count);

        var inputValues = new Dictionary<string, InputValue>
        {
            ["cluster_name"] = new InputValue("\"prod-cluster\"", "Main cluster"),
            ["node_count"] = "3",
        };

        var inputHcl = emitter.EmitInputValues(inputValues);
        Assert.Contains("\"prod-cluster\"", inputHcl);
        Assert.Contains("# Main cluster", inputHcl);
        Assert.Contains("# (Required) Name of the compute cluster.", inputHcl);
    }
}
