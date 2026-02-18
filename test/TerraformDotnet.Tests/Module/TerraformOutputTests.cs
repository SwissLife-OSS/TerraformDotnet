using System.Text;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformOutputTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void SimpleOutput()
    {
        var module = Parse("""
            output "instance_id" {
              value = cloud_instance.main.id
            }
            """);

        var o = Assert.Single(module.Outputs);
        Assert.Equal("instance_id", o.Name);
        Assert.NotNull(o.Value);
        Assert.Null(o.Description);
        Assert.False(o.IsSensitive);
        Assert.Null(o.DependsOn);
    }

    [Fact]
    public void OutputWithDescription()
    {
        var module = Parse("""
            output "endpoint" {
              value       = db_cluster.main.endpoint
              description = "The primary endpoint."
            }
            """);

        Assert.Equal("The primary endpoint.", module.Outputs[0].Description);
    }

    [Fact]
    public void SensitiveOutput()
    {
        var module = Parse("""
            output "secret" {
              value     = db_cluster.main.connection_string
              sensitive = true
            }
            """);

        Assert.True(module.Outputs[0].IsSensitive);
    }

    [Fact]
    public void OutputWithDependsOn()
    {
        var module = Parse("""
            output "endpoint" {
              value      = db_cluster.main.endpoint
              depends_on = [db_cluster.main]
            }
            """);

        var o = module.Outputs[0];
        Assert.NotNull(o.DependsOn);
        Assert.Single(o.DependsOn);
        Assert.Equal("db_cluster.main", o.DependsOn[0]);
    }

    [Fact]
    public void MultipleOutputs()
    {
        var module = Parse("""
            output "id" {
              value = res.main.id
            }

            output "name" {
              value = res.main.name
            }

            output "ip" {
              value       = res.main.ip
              description = "Public IP."
              sensitive   = true
            }
            """);

        Assert.Equal(3, module.Outputs.Count);
        Assert.Equal("id", module.Outputs[0].Name);
        Assert.Equal("name", module.Outputs[1].Name);
        Assert.Equal("ip", module.Outputs[2].Name);
        Assert.True(module.Outputs[2].IsSensitive);
    }
}
