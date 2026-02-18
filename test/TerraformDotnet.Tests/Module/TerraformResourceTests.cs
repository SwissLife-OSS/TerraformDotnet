using System.Text;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformResourceTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void SimpleResource()
    {
        var module = Parse("""
            resource "cloud_instance" "main" {
              name   = "my-instance"
              region = "us-east-1"
            }
            """);

        var r = Assert.Single(module.Resources);
        Assert.Equal("cloud_instance", r.Type);
        Assert.Equal("main", r.Name);
        Assert.NotNull(r.Body);
        Assert.Null(r.Count);
        Assert.Null(r.ForEach);
        Assert.Null(r.DependsOn);
        Assert.Null(r.Provider);
    }

    [Fact]
    public void ResourceWithCount()
    {
        var module = Parse("""
            resource "cloud_instance" "workers" {
              count  = 3
              name   = "worker-${count.index}"
            }
            """);

        var r = Assert.Single(module.Resources);
        Assert.NotNull(r.Count);
    }

    [Fact]
    public void ResourceWithForEach()
    {
        var module = Parse("""
            resource "cloud_rule" "rules" {
              for_each = toset(var.cidrs)
              cidr     = each.value
            }
            """);

        var r = Assert.Single(module.Resources);
        Assert.NotNull(r.ForEach);
    }

    [Fact]
    public void ResourceWithDependsOn()
    {
        var module = Parse("""
            resource "cloud_disk" "data" {
              name       = "data-disk"
              depends_on = [cloud_instance.main]
            }
            """);

        var r = Assert.Single(module.Resources);
        Assert.NotNull(r.DependsOn);
        Assert.Single(r.DependsOn);
        Assert.Equal("cloud_instance.main", r.DependsOn[0]);
    }

    [Fact]
    public void ResourceWithProvider()
    {
        var module = Parse("""
            resource "cloud_rule" "west" {
              name     = "rule"
              provider = cloud.west
            }
            """);

        var r = Assert.Single(module.Resources);
        Assert.Equal("cloud.west", r.Provider);
    }

    [Fact]
    public void MultipleResources()
    {
        var module = Parse("""
            resource "cloud_instance" "main" {
              name = "main"
            }

            resource "cloud_disk" "data" {
              name = "data"
            }

            resource "cloud_network" "vnet" {
              name = "vnet"
            }
            """);

        Assert.Equal(3, module.Resources.Count);
    }
}
