using System.Text;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformProviderTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void ProviderWithSourceAndVersion()
    {
        var module = Parse("""
            terraform {
              required_providers {
                cloud = {
                  source  = "registry.example.com/example/cloud"
                  version = ">= 2.0"
                }
              }
            }
            """);

        var p = Assert.Single(module.ProviderRequirements);
        Assert.Equal("cloud", p.Name);
        Assert.Equal("registry.example.com/example/cloud", p.Source);
        Assert.Equal(">= 2.0", p.Version);
    }

    [Fact]
    public void RequiredVersionString()
    {
        var module = Parse("""
            terraform {
              required_version = ">= 1.5.0"
            }
            """);

        Assert.Equal(">= 1.5.0", module.RequiredTerraformVersion);
    }

    [Fact]
    public void MultipleProviders()
    {
        var module = Parse("""
            terraform {
              required_providers {
                cloud = {
                  source  = "registry.example.com/example/cloud"
                  version = ">= 2.0"
                }
                db = {
                  source  = "registry.example.com/example/db"
                  version = "~> 4.0"
                }
              }
            }
            """);

        Assert.Equal(2, module.ProviderRequirements.Count);
        Assert.Equal("cloud", module.ProviderRequirements[0].Name);
        Assert.Equal("db", module.ProviderRequirements[1].Name);
    }

    [Fact]
    public void NoTerraformBlock_ReturnEmptyAndNull()
    {
        var module = Parse("""
            variable "x" {
              type = string
            }
            """);

        Assert.Empty(module.ProviderRequirements);
        Assert.Null(module.RequiredTerraformVersion);
    }

    [Fact]
    public void TerraformBlockWithNoRequiredProviders()
    {
        var module = Parse("""
            terraform {
              required_version = ">= 1.0"
            }
            """);

        Assert.Empty(module.ProviderRequirements);
        Assert.Equal(">= 1.0", module.RequiredTerraformVersion);
    }
}
