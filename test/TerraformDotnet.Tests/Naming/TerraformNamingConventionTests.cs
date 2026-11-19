using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;
using TerraformDotnet.Naming;

namespace TerraformDotnet.Tests.Naming;

public class TerraformNamingConventionTests
{
    private static readonly NameFormat Hyphens = NameFormat.Custom("^[a-z0-9-]+$");

    private static readonly TerraformNamingConvention HyphenatedBlocks = TerraformNamingConvention.Default with
    {
        Resource = Hyphens,
        Data = Hyphens,
        Module = Hyphens,
    };

    private static HclFile Parse(string hcl) => HclFile.Load(Encoding.UTF8.GetBytes(hcl));

    [Fact]
    public void DefaultIsSnakeCaseForEveryKind()
    {
        foreach (TerraformNameKind kind in Enum.GetValues<TerraformNameKind>())
        {
            Assert.Same(NameFormat.SnakeCase, TerraformNamingConvention.Default.GetFormat(kind));
        }
    }

    [Fact]
    public void UniformUsesOneFormatForEveryKind()
    {
        TerraformNamingConvention convention = TerraformNamingConvention.Uniform(Hyphens);

        foreach (TerraformNameKind kind in Enum.GetValues<TerraformNameKind>())
        {
            Assert.Same(Hyphens, convention.GetFormat(kind));
        }
    }

    [Fact]
    public void FormatUsesTheFormatOfTheKind()
    {
        Assert.Equal("dns-web-app", HyphenatedBlocks.Format(TerraformNameKind.Module, "dns", "web_app"));
        Assert.Equal("instances_dns_web_app", HyphenatedBlocks.Format(TerraformNameKind.Variable, "instances", "dns-web-app"));
    }

    [Fact]
    public void IsValidUsesTheFormatOfTheKind()
    {
        Assert.True(HyphenatedBlocks.IsValid(TerraformNameKind.Module, "dns-web"));
        Assert.False(HyphenatedBlocks.IsValid(TerraformNameKind.Module, "dns_web"));
        Assert.True(HyphenatedBlocks.IsValid(TerraformNameKind.Variable, "dns_web"));
        Assert.False(HyphenatedBlocks.IsValid(TerraformNameKind.Variable, "dns-web"));
    }

    [Fact]
    public void ConventionsAreEqualByValue()
    {
        Assert.Equal(TerraformNamingConvention.Default, new TerraformNamingConvention());
        Assert.Equal(
            TerraformNamingConvention.Default with { Module = NameFormat.Custom("^[a-z0-9-]+$") },
            TerraformNamingConvention.Default with { Module = Hyphens });
        Assert.NotEqual(TerraformNamingConvention.Default, HyphenatedBlocks);
    }

    [Theory]
    [InlineData("web", true)]
    [InlineData("_web-app_1", true)]
    [InlineData("1web", false)]
    [InlineData("web.app", false)]
    [InlineData("-web", false)]
    [InlineData("", false)]
    public void IsIdentifierChecksTerraformNames(string name, bool expected)
    {
        Assert.Equal(expected, TerraformNamingConvention.IsIdentifier(name));
    }

    [Fact]
    public void ValidateFileFindsEveryKindOfName()
    {
        HclFile file = Parse("""
            resource "aws_instance" "Web" {}

            data "aws_ami" "Latest" {}

            module "Network" {
              source = "./network"
            }

            variable "Region" {}

            output "Address" {
              value = 1
            }

            locals {
              Zone  = "a"
              valid = "b"
            }

            check "Health" {
              data "http" "Probe" {
                url = "https://example.com"
              }
            }
            """);

        IReadOnlyList<NamingViolation> violations = TerraformNamingConvention.Default.Validate(file);

        Assert.Equal(
            [
                (1, "aws_instance.Web", "resource name `Web` must match the following format: snake_case"),
                (3, "data.aws_ami.Latest", "data name `Latest` must match the following format: snake_case"),
                (5, "module.Network", "module name `Network` must match the following format: snake_case"),
                (9, "var.Region", "variable name `Region` must match the following format: snake_case"),
                (11, "output.Address", "output name `Address` must match the following format: snake_case"),
                (16, "local.Zone", "local value name `Zone` must match the following format: snake_case"),
                (20, "check.Health", "check name `Health` must match the following format: snake_case"),
                (21, "data.http.Probe", "data name `Probe` must match the following format: snake_case"),
            ],
            violations.Select(v => (v.Line ?? 0, v.Address, v.Message)));
        Assert.Equal(TerraformNameKind.Resource, violations[0].Kind);
        Assert.Equal("aws_instance", violations[0].Type);
        Assert.Equal("Web", violations[0].Name);
        Assert.Same(NameFormat.SnakeCase, violations[0].Format);
    }

    [Fact]
    public void ValidateFileAcceptsConformingNames()
    {
        HclFile file = Parse("""
            resource "aws_instance" "web-1" {}

            module "dns-web" {
              source = "./dns"
            }

            variable "instances_dns_web" {}

            locals {
              zone_name = "a"
            }
            """);

        Assert.Empty(HyphenatedBlocks.Validate(file));
    }

    [Fact]
    public void ValidateFileSkipsKindsThatAreNotChecked()
    {
        HclFile file = Parse("""
            module "Any.Name" {
              source = "./dns"
            }

            variable "Region" {}
            """);
        TerraformNamingConvention convention = TerraformNamingConvention.Default with { Module = NameFormat.None };

        NamingViolation violation = Assert.Single(convention.Validate(file));

        Assert.Equal("var.Region", violation.Address);
    }

    [Fact]
    public void ValidateFileReportsACustomPatternAsRegExp()
    {
        HclFile file = Parse("""
            module "dns_web" {
              source = "./dns"
            }
            """);

        NamingViolation violation = Assert.Single(HyphenatedBlocks.Validate(file));

        Assert.Equal("module name `dns_web` must match the following RegExp: ^[a-z0-9-]+$", violation.Message);
    }

    [Fact]
    public void ValidateFileBuiltInCodeHasNoLines()
    {
        var file = new HclFile();
        file.Body.Blocks.Add(new HclBlock { Type = "variable", Labels = { "Region" } });

        NamingViolation violation = Assert.Single(TerraformNamingConvention.Default.Validate(file));

        Assert.Null(violation.Line);
    }

    [Fact]
    public void ValidateModuleFindsTheNamesOfTheModel()
    {
        TerraformModule module = TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes("""
            resource "aws_instance" "web_1" {}

            data "aws_ami" "latest-ami" {}

            module "dns_web" {
              source = "./dns"
            }

            variable "region-name" {}

            output "address" {
              value = 1
            }

            locals {
              zoneName = "a"
            }
            """));

        IReadOnlyList<NamingViolation> violations = HyphenatedBlocks.Validate(module);

        Assert.Equal(
            ["aws_instance.web_1", "module.dns_web", "var.region-name", "local.zoneName"],
            violations.Select(v => v.Address));
        Assert.All(violations, v => Assert.Null(v.Line));
    }
}
