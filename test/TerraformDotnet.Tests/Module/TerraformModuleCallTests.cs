using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformModuleCallTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void ParsesBuiltInAndInputArguments()
    {
        var module = Parse("""
            module "network" {
              source  = "hashicorp/consul/aws"
              version = "0.11.0"

              count      = 2
              name       = local.name
              cidr_block = var.cidr_block

              providers = {
                aws = aws.west
              }

              depends_on = [aws_vpc.main]
            }
            """);

        var call = Assert.Single(module.ModuleCalls);

        Assert.Equal("network", call.Name);
        Assert.IsType<HclLiteralExpression>(call.Source);
        Assert.IsType<HclLiteralExpression>(call.Version);
        Assert.NotNull(call.Count);
        Assert.Null(call.ForEach);
        Assert.IsType<HclObjectExpression>(call.Providers);
        Assert.IsType<HclTupleExpression>(call.DependsOn);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Contains("name", call.Arguments.Keys);
        Assert.Contains("cidr_block", call.Arguments.Keys);
        Assert.Same(call.Arguments["name"], call.Body.Attributes.Single(a => a.Name == "name").Value);
    }

    [Fact]
    public void PreservesForEachAndCurrentTerraformArguments()
    {
        var module = Parse("""
            module "bucket" {
              source = "./modules/bucket"

              for_each                   = var.buckets
              ignore_nested_deprecations = true
              name                       = each.key
            }
            """);

        var call = Assert.Single(module.ModuleCalls);

        Assert.NotNull(call.ForEach);
        Assert.NotNull(call.IgnoreNestedDeprecations);
        Assert.Single(call.Arguments);
        Assert.Contains("name", call.Arguments.Keys);
    }

    [Fact]
    public void PreservesSourceAndEmptyDependsOnExpressions()
    {
        var module = Parse("""
            module "example" {
              source     = var.module_source
              depends_on = []
            }
            """);

        var call = Assert.Single(module.ModuleCalls);

        Assert.IsType<HclAttributeAccessExpression>(call.Source);
        var dependencies = Assert.IsType<HclTupleExpression>(call.DependsOn);
        Assert.Empty(dependencies.Elements);
    }

    [Fact]
    public void EscapingBlockTreatsBuiltInNameAsModuleInput()
    {
        var module = Parse("""
            module "example" {
              source = "./modules/example"

              _ {
                providers = var.providers
              }
            }
            """);

        var call = Assert.Single(module.ModuleCalls);

        Assert.Null(call.Providers);
        Assert.Same(
            call.Arguments["providers"],
            Assert.Single(call.Body.Blocks).Body.Attributes.Single().Value);
    }

    [Fact]
    public void ModuleCallsFromMultipleFilesAreMerged()
    {
        var first = HclFile.Load("""
            module "network" {
              source = "./modules/network"
            }
            """u8);
        var second = HclFile.Load("""
            module "compute" {
              source = "./modules/compute"
            }
            """u8);

        var module = TerraformModule.LoadFromFiles([first, second]);

        Assert.Collection(
            module.ModuleCalls,
            call => Assert.Equal("network", call.Name),
            call => Assert.Equal("compute", call.Name));
    }

    [Fact]
    public void DuplicateEscapingBlocksAreRejected()
    {
        var exception = Assert.Throws<FormatException>(() => Parse("""
            module "example" {
              source = "./modules/example"

              _ {
                source = var.first_source
              }

              _ {
                version = var.version
              }
            }
            """));

        Assert.Contains("cannot contain more than one '_' escaping block", exception.Message);
    }

    [Fact]
    public void MissingSourceIsRejected()
    {
        var exception = Assert.Throws<FormatException>(() => Parse("""
            module "example" {
              name = "example"
            }
            """));

        Assert.Contains("missing required 'source'", exception.Message);
    }

    [Fact]
    public void CountAndForEachAreRejectedTogether()
    {
        var exception = Assert.Throws<FormatException>(() => Parse("""
            module "example" {
              source   = "./modules/example"
              count    = 1
              for_each = var.examples
            }
            """));

        Assert.Contains("cannot use both 'count' and 'for_each'", exception.Message);
    }
}
