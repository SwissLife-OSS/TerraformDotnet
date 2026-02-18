using System.Text;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Module;

public class TerraformDataSourceTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void SimpleDataSource()
    {
        var module = Parse("""
            data "cloud_image" "latest" {
              name   = "ubuntu-22.04"
              region = "us-east-1"
            }
            """);

        var d = Assert.Single(module.DataSources);
        Assert.Equal("cloud_image", d.Type);
        Assert.Equal("latest", d.Name);
        Assert.NotNull(d.Body);
        Assert.Null(d.Count);
        Assert.Null(d.ForEach);
    }

    [Fact]
    public void DataSourceWithCount()
    {
        var module = Parse("""
            data "cloud_secret" "secrets" {
              count = 2
              name  = "secret-${count.index}"
            }
            """);

        var d = Assert.Single(module.DataSources);
        Assert.NotNull(d.Count);
    }

    [Fact]
    public void DataSourceWithEmptyBody()
    {
        var module = Parse("""
            data "cloud_identity" "current" {
            }
            """);

        var d = Assert.Single(module.DataSources);
        Assert.Equal("cloud_identity", d.Type);
        Assert.Equal("current", d.Name);
    }

    [Fact]
    public void MultipleDataSources()
    {
        var module = Parse("""
            data "cloud_image" "a" {
              name = "image-a"
            }

            data "cloud_secret" "b" {
              name = "secret-b"
            }
            """);

        Assert.Equal(2, module.DataSources.Count);
    }
}
