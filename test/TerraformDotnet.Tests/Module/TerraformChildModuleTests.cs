using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformChildModuleTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void SimpleResource()
    {
        var module = Parse("""
            module "main" {
              source = "../../../module_source"
              name   = "my-instance"
              region = "us-east-1"
            }
            """);

        var r = Assert.Single(module.ChildModules);
        Assert.Equal("main", r.Name);
        Assert.Equal("../../../module_source", (r.Source as HclLiteralExpression)?.Value);
        Assert.NotNull(r.Body);
        Assert.Null(r.Count);
        Assert.Null(r.ForEach);
        Assert.Null(r.DependsOn);
    }

    [Fact]
    public void ModuleWithVersion()
    {
        var module = Parse("""
                           module "workers" {
                             source = "example_registry_module"
                             version = "1.0.0"
                             count  = 3
                             name   = "worker-${count.index}"
                           }
                           """);

        var r = Assert.Single(module.ChildModules);
        Assert.NotNull(r.Version);
    }

    [Fact]
    public void ModuleWithProviders()
    {
        var module = Parse("""
                           module "workers" {
                             source = "example_registry_module"
                             providers = {
                                aws = aws.alias
                             }
                             version = "1.0.0"
                             count  = 3
                             name   = "worker-${count.index}"
                           }
                           """);

        var r = Assert.Single(module.ChildModules);
        Assert.NotNull(r.Providers);
    }

     [Fact]
     public void ModuleWithCount()
     {
         var module = Parse("""
             module "workers" {
               source = "../../../module_source"
               count  = 3
               name   = "worker-${count.index}"
             }
             """);

         var r = Assert.Single(module.ChildModules);
         Assert.NotNull(r.Count);
     }

     [Fact]
     public void ModuleWithForEach()
     {
         var module = Parse("""
             module "rules" {
               source   = "../../../module_source"
               for_each = toset(var.cidrs)
               cidr     = each.value
             }
             """);

         var r = Assert.Single(module.ChildModules);
         Assert.NotNull(r.ForEach);
     }

     [Fact]
     public void ResourceWithDependsOn()
     {
         var module = Parse("""
             module "data" {
               source     = "../../../module_source"
               name       = "data-disk"
               depends_on = [cloud_instance.main]
             }
             """);

         var r = Assert.Single(module.ChildModules);
         Assert.NotNull(r.DependsOn);
         Assert.Single(r.DependsOn);
         Assert.Equal("cloud_instance.main", r.DependsOn[0]);
     }

     [Fact]
     public void MultipleChildModules()
     {
         var module = Parse("""
             module "main" {
               source = "../../../module_source"
               name   = "main"
             }

             module "data" {
               source = "../../../module_source"
               name   = "data"
             }

             module "vnet" {
               source = "../../../module_source"
               name   = "vnet"
             }
             """);

         Assert.Equal(3, module.ChildModules.Count);
     }
}
