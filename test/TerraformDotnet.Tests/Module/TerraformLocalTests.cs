using System.Text;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformLocalTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void StringLocal()
    {
        var module = Parse("""
            locals {
              instance_name = "my-instance"
            }
            """);

        var local = Assert.Single(module.Locals);
        Assert.Equal("instance_name", local.Name);
        Assert.NotNull(local.Value);
    }

    [Fact]
    public void ExpressionLocal()
    {
        var module = Parse("""
            locals {
              full_name = "${var.project}-${var.region}"
            }
            """);

        var local = Assert.Single(module.Locals);
        Assert.Equal("full_name", local.Name);
    }

    [Fact]
    public void MultipleLocals()
    {
        var module = Parse("""
            locals {
              name = "test"
              tags = merge(var.labels, { managed_by = "terraform" })
            }
            """);

        Assert.Equal(2, module.Locals.Count);
        Assert.Equal("name", module.Locals[0].Name);
        Assert.Equal("tags", module.Locals[1].Name);
    }

    [Fact]
    public void MultipleLocalsBlocks()
    {
        var module = Parse("""
            locals {
              alpha = "a"
            }

            locals {
              beta = "b"
            }
            """);

        Assert.Equal(2, module.Locals.Count);
        Assert.Equal("alpha", module.Locals[0].Name);
        Assert.Equal("beta", module.Locals[1].Name);
    }
}
