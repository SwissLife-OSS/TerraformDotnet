using System.Text;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformModuleTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void LoadFromContent_SingleFile()
    {
        var module = Parse("""
            variable "name" {
              type = string
            }

            variable "count" {
              type    = number
              default = 1
            }

            output "id" {
              value = res.main.id
            }
            """);

        Assert.Equal(2, module.Variables.Count);
        Assert.Single(module.RequiredVariables);
        Assert.Single(module.OptionalVariables);
        Assert.Single(module.Outputs);
    }

    [Fact]
    public void LoadFromMultipleFiles_MergesVariables()
    {
        var file1 = Hcl.Nodes.HclFile.Load(Encoding.UTF8.GetBytes("""
            variable "project" {
              type = string
            }
            """));

        var file2 = Hcl.Nodes.HclFile.Load(Encoding.UTF8.GetBytes("""
            variable "region" {
              type = string
            }

            variable "tier" {
              type    = string
              default = "basic"
            }
            """));

        var module = TerraformModule.LoadFromFiles([file1, file2]);

        Assert.Equal(3, module.Variables.Count);
        Assert.Equal(2, module.RequiredVariables.Count);
        Assert.Single(module.OptionalVariables);
    }

    [Fact]
    public void RequiredOptionalFiltering()
    {
        var module = Parse("""
            variable "required_a" {
              type = string
            }

            variable "required_b" {
              type = string
            }

            variable "optional_a" {
              type    = string
              default = "x"
            }

            variable "optional_b" {
              type    = number
              default = 42
            }

            variable "optional_c" {
              type    = bool
              default = false
            }
            """);

        Assert.Equal(5, module.Variables.Count);
        Assert.Equal(2, module.RequiredVariables.Count);
        Assert.Equal(3, module.OptionalVariables.Count);
    }

    [Fact]
    public void EmptyModule()
    {
        var module = Parse("");

        Assert.Empty(module.Variables);
        Assert.Empty(module.RequiredVariables);
        Assert.Empty(module.OptionalVariables);
        Assert.Empty(module.Outputs);
        Assert.Empty(module.Resources);
        Assert.Empty(module.DataSources);
        Assert.Empty(module.Locals);
        Assert.Empty(module.ProviderRequirements);
        Assert.Null(module.RequiredTerraformVersion);
    }

    [Fact]
    public void ModuleWithAllBlockTypes()
    {
        var module = Parse("""
            terraform {
              required_version = ">= 1.3"

              required_providers {
                mycloud = {
                  source  = "example.com/test/mycloud"
                  version = ">= 1.0"
                }
              }
            }

            variable "name" {
              type = string
            }

            locals {
              full_name = var.name
            }

            resource "mycloud_instance" "main" {
              name = local.full_name
            }

            data "mycloud_image" "latest" {
              name = "ubuntu"
            }

            output "instance_id" {
              value = mycloud_instance.main.id
            }
            """);

        Assert.Single(module.Variables);
        Assert.Single(module.Outputs);
        Assert.Single(module.Resources);
        Assert.Single(module.DataSources);
        Assert.Single(module.Locals);
        Assert.Single(module.ProviderRequirements);
        Assert.Equal(">= 1.3", module.RequiredTerraformVersion);
    }

    [Fact]
    public void LoadFromDirectory_ComputeModule()
    {
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "__assets__", "compute-module");
        var module = TerraformModule.LoadFromDirectory(assetsDir);

        // compute-module has: 4 required + 7 optional variables in common + 3 optional in network + 3 optional in monitoring
        Assert.True(module.Variables.Count >= 10, $"Expected at least 10 variables, got {module.Variables.Count}");
        Assert.True(module.RequiredVariables.Count >= 3,
            $"Expected at least 3 required vars, got {module.RequiredVariables.Count}");
        Assert.True(module.OptionalVariables.Count >= 7,
            $"Expected at least 7 optional vars, got {module.OptionalVariables.Count}");

        // Outputs: 4
        Assert.Equal(4, module.Outputs.Count);

        // Resources: cloud_instance, cloud_disk, cloud_network_rule, cloud_private_endpoint, cloud_vnet, cloud_subnet = 6
        Assert.Equal(6, module.Resources.Count);

        // Data sources: cloud_image, cloud_identity = 2
        Assert.Equal(2, module.DataSources.Count);

        // Locals: instance_name, full_labels = 2
        Assert.Equal(2, module.Locals.Count);

        // Provider requirements: cloud
        Assert.Single(module.ProviderRequirements);
        Assert.Equal("cloud", module.ProviderRequirements[0].Name);

        // Required version
        Assert.Equal(">= 1.5.0", module.RequiredTerraformVersion);
    }

    [Fact]
    public void LoadFromDirectory_DatabaseModule()
    {
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "__assets__", "database-module");
        var module = TerraformModule.LoadFromDirectory(assetsDir);

        // db_name and db_password are required
        Assert.Equal(2, module.RequiredVariables.Count);
        Assert.Contains(module.RequiredVariables, v => v.Name == "db_name");
        Assert.Contains(module.RequiredVariables, v => v.Name == "db_password");

        // db_password is sensitive
        var passwordVar = module.Variables.First(v => v.Name == "db_password");
        Assert.True(passwordVar.IsSensitive);

        // backup_retention has a validation block
        var retentionVar = module.Variables.First(v => v.Name == "backup_retention");
        Assert.NotNull(retentionVar.Validation);

        // db_nullable_field is nullable
        var nullableVar = module.Variables.First(v => v.Name == "db_nullable_field");
        Assert.True(nullableVar.IsNullable);

        // Outputs: 2
        Assert.Equal(2, module.Outputs.Count);
        Assert.True(module.Outputs.First(o => o.Name == "cluster_endpoint").IsSensitive);
    }

    [Fact]
    public void LoadFromDirectory_EmptyModule()
    {
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "__assets__", "empty-module");
        var module = TerraformModule.LoadFromDirectory(assetsDir);

        Assert.Empty(module.Variables);
        Assert.Empty(module.Outputs);
        Assert.Single(module.Resources);
    }

    [Fact]
    public void LoadFromDirectory_NonExistentPath_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            TerraformModule.LoadFromDirectory("/nonexistent/path/12345"));
    }
}
