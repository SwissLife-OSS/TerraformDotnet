using System.Text;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Naming;

namespace TerraformDotnet.Tests.Naming;

public class TflintNamingConfigTests
{
    private const string HyphenatedBlocksConfig = """
        config {
          # Use only the rules which are explicitly enabled
          disabled_by_default = true
        }

        plugin "terraform" {
          enabled = true
          version = "0.9.1"
          source  = "github.com/terraform-linters/tflint-ruleset-terraform"
        }

        rule "terraform_typed_variables" {
          enabled = false
        }

        rule "terraform_naming_convention" {
          enabled = true

          resource {
            format = "resource_module_data_format"
          }

          module {
            format = "resource_module_data_format"
          }

          data {
            format = "resource_module_data_format"
          }

          locals {
            format = "snake_case"
          }

          variable {
            format = "snake_case"
          }

          output {
            format = "snake_case"
          }

          custom_formats = {
            resource_module_data_format = {
              description = "All Terraform resource module names and data sources are using '-' as separators"
              regex       = "^[a-z0-9-]+$"
            }
          }
        }
        """;

    private static TerraformNamingConvention Read(string rule, TerraformNamingConvention? fallback = null) =>
        TerraformNamingConvention.FromTflintConfig(rule, fallback)
        ?? throw new InvalidOperationException("The rule is not enabled.");

    private static string[] Lint(string config, string terraform) =>
    [
        .. Read(config)
            .Validate(HclFile.Load(Encoding.UTF8.GetBytes(terraform)))
            .Select(violation => violation.Message)
    ];

    [Fact]
    public void ReadsACustomFormatPerBlockType()
    {
        TerraformNamingConvention convention = Read(HyphenatedBlocksConfig);

        foreach (NameFormat format in new[] { convention.Resource, convention.Module, convention.Data })
        {
            Assert.Equal("^[a-z0-9-]+$", format.Pattern);
            Assert.Equal(NameStyle.KebabCase, format.Style);
            Assert.Equal(
                "All Terraform resource module names and data sources are using '-' as separators",
                format.Name);
        }

        Assert.Same(NameFormat.SnakeCase, convention.Variable);
        Assert.Same(NameFormat.SnakeCase, convention.Output);
        Assert.Same(NameFormat.SnakeCase, convention.Local);
        Assert.Same(NameFormat.SnakeCase, convention.Check);
    }

    [Fact]
    public void GeneratesNamesThatPassTheRule()
    {
        TerraformNamingConvention convention = Read(HyphenatedBlocksConfig);

        Assert.Equal("dns-bff-admin", convention.Format(TerraformNameKind.Module, "dns", "bff_admin"));
        Assert.Equal("instances_dns_bff_admin", convention.Format(TerraformNameKind.Variable, "instances", "dns-bff-admin"));
    }

    [Fact]
    public void ReportsTheMessageOfTflint()
    {
        string[] messages = Lint(
            HyphenatedBlocksConfig,
            """
            module "dns_bff_admin" {
              source = "./cloudflare"
            }
            """);

        Assert.Equal(
            [
                "module name `dns_bff_admin` must match the following format: " +
                "All Terraform resource module names and data sources are using '-' as separators"
            ],
            messages);
    }

    [Fact]
    public void DefaultRuleEnforcesSnakeCaseForAllBlocks()
    {
        string[] messages = Lint(
            """
            rule "terraform_naming_convention" {
              enabled = true
            }
            """,
            """
            data "aws_eip" "camelCase" {
            }

            data "aws_eip" "valid_name" {
            }
            """);

        Assert.Equal(["data name `camelCase` must match the following format: snake_case"], messages);
    }

    [Fact]
    public void CustomExpressionAppliesToAllBlocks()
    {
        string[] messages = Lint(
            """
            rule "terraform_naming_convention" {
              enabled = true

              custom = "^[a-zA-Z]+([_-][a-zA-Z]+)*$"
            }
            """,
            """
            resource "aws_eip" "Invalid_Name_With_Number123" {
            }

            resource "aws_eip" "Name-With_Dash" {
            }
            """);

        Assert.Equal(
            [
                "resource name `Invalid_Name_With_Number123` must match the following RegExp: " +
                "^[a-zA-Z]+([_-][a-zA-Z]+)*$"
            ],
            messages);
    }

    [Fact]
    public void CustomFormatAppliesToAllBlocks()
    {
        string[] messages = Lint(
            """
            rule "terraform_naming_convention" {
              enabled = true
              format = "custom_format"

              custom_formats = {
                custom_format = {
                  description = "Custom Format"
                  regex       = "^[a-zA-Z]+([_-][a-zA-Z]+)*$"
                }
              }
            }
            """,
            """
            resource "aws_eip" "Invalid_Name_With_Number123" {
            }

            resource "aws_eip" "Name-With_Dash" {
            }
            """);

        Assert.Equal(
            ["resource name `Invalid_Name_With_Number123` must match the following format: Custom Format"],
            messages);
    }

    [Fact]
    public void BlockOverridesTheDefaultForItsType()
    {
        string[] messages = Lint(
            """
            rule "terraform_naming_convention" {
              enabled = true

              module {
                custom = "^[a-zA-Z]+(_[a-zA-Z]+)*$"
              }
            }
            """,
            """
            // data name enforced with default snake_case
            data "aws_eip" "eip_1a" {
            }

            module "valid_module" {
              source = ""
            }

            module "invalid_module_with_number_1a" {
              source = ""
            }
            """);

        Assert.Equal(
            [
                "module name `invalid_module_with_number_1a` must match the following RegExp: " +
                "^[a-zA-Z]+(_[a-zA-Z]+)*$"
            ],
            messages);
    }

    [Fact]
    public void NoneDisablesTheCheckForItsType()
    {
        string[] messages = Lint(
            """
            rule "terraform_naming_convention" {
              enabled = true

              module {
                format = "none"
              }
            }
            """,
            """
            data "aws_eip" "eip_1a" {
            }

            module "Valid_Name-Not-Enforced" {
              source = ""
            }
            """);

        Assert.Empty(messages);
    }

    [Fact]
    public void NoneForAllBlocksStillEnforcesASpecificType()
    {
        string[] messages = Lint(
            """
            rule "terraform_naming_convention" {
              enabled = true
              format  = "none"

              locals {
                format = "snake_case"
              }
            }
            """,
            """
            data "aws_eip" "EIP_1a" {
            }

            resource "aws_eip" "EIP_1b" {
            }

            locals {
              valid_name   = "valid"
              invalid-name = "dashes are not allowed with snake_case"
            }
            """);

        Assert.Equal(["local value name `invalid-name` must match the following format: snake_case"], messages);
    }

    [Fact]
    public void CustomWinsOverFormat()
    {
        TerraformNamingConvention convention = Read("""
            rule "terraform_naming_convention" {
              enabled = true

              module {
                format = "snake_case"
                custom = "^[a-z0-9-]+$"
              }
            }
            """);

        Assert.Equal("^[a-z0-9-]+$", convention.Module.Pattern);
    }

    [Fact]
    public void PredefinedFormatsIgnoreCase()
    {
        TerraformNamingConvention convention = Read("""
            rule "terraform_naming_convention" {
              enabled = true
              format  = "Mixed_Snake_Case"
            }
            """);

        Assert.Same(NameFormat.MixedSnakeCase, convention.Resource);
    }

    [Fact]
    public void DecodesEscapesOfThePattern()
    {
        TerraformNamingConvention convention = Read("""
            rule "terraform_naming_convention" {
              enabled = true
              custom  = "^[a-z]+\\d$"
            }
            """);

        Assert.Equal(@"^[a-z]+\d$", convention.Module.Pattern);
        Assert.True(convention.IsValid(TerraformNameKind.Module, "web1"));
    }

    [Fact]
    public void FallbackDecidesTheStyleTheRuleLeavesOpen()
    {
        const string config = """
            rule "terraform_naming_convention" {
              enabled = true
              format  = "none"

              module {
                custom = "^[a-z0-9_-]+$"
              }

              variable {
                format = "snake_case"
              }
            }
            """;
        TerraformNamingConvention hyphens = TerraformNamingConvention.Uniform(NameFormat.KebabCase);

        TerraformNamingConvention byDefault = Read(config);
        TerraformNamingConvention withFallback = Read(config, hyphens);

        Assert.Equal("web_app", byDefault.Format(TerraformNameKind.Resource, "Web App"));
        Assert.Equal("web_app", byDefault.Format(TerraformNameKind.Module, "Web App"));
        Assert.Equal("web-app", withFallback.Format(TerraformNameKind.Resource, "Web App"));
        Assert.Equal("web-app", withFallback.Format(TerraformNameKind.Module, "Web App"));

        // The fallback never overrides what the rule requires.
        Assert.Same(NameFormat.SnakeCase, withFallback.Variable);
        Assert.False(withFallback.Resource.IsChecked);
    }

    [Fact]
    public void FallbackStyleIsNotUsedWhenThePatternRejectsIt()
    {
        TerraformNamingConvention convention = Read(
            """
            rule "terraform_naming_convention" {
              enabled = true
              custom  = "^[a-z0-9-]+$"
            }
            """,
            TerraformNamingConvention.Default);

        Assert.Equal(NameStyle.KebabCase, convention.Module.Style);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""
        rule "terraform_naming_convention" {
          enabled = false
        }
        """)]
    [InlineData("""
        plugin "terraform" {
          enabled = true
          preset  = "recommended"
        }
        """)]
    [InlineData("""
        config {
          disabled_by_default = true
        }

        plugin "terraform" {
          enabled = true
          preset  = "all"
        }
        """)]
    public void ReturnsNullWhenTheRuleIsNotEnabled(string config)
    {
        Assert.Null(TerraformNamingConvention.FromTflintConfig(config));
    }

    [Fact]
    public void PresetAllEnablesTheRuleWithItsDefaults()
    {
        TerraformNamingConvention? convention = TerraformNamingConvention.FromTflintConfig("""
            plugin "terraform" {
              enabled = true
              preset  = "all"
            }
            """);

        Assert.Equal(TerraformNamingConvention.Default, convention);
    }

    [Fact]
    public void ReadsAParsedConfig()
    {
        HclFile config = HclFile.Load(Encoding.UTF8.GetBytes(HyphenatedBlocksConfig));

        Assert.Equal(Read(HyphenatedBlocksConfig), TerraformNamingConvention.FromTflintConfig(config));
    }

    [Theory]
    [InlineData(
        """
        rule "terraform_naming_convention" {
          enabled = true
          format  = "kebab_case"
        }
        """,
        "Invalid default configuration: `kebab_case` is unsupported format")]
    [InlineData(
        """
        rule "terraform_naming_convention" {
          enabled = true

          module {
          }
        }
        """,
        "Invalid module configuration: `` is unsupported format")]
    [InlineData(
        """
        rule "terraform_naming_convention" {
          enabled = true

          locals {
            format = "NONE"
          }
        }
        """,
        "Invalid local value configuration: `NONE` is unsupported format")]
    [InlineData(
        """
        rule "terraform_naming_convention" {
          enabled = true
          format  = 1
        }
        """,
        "'format' must be a string.")]
    [InlineData(
        """
        rule "terraform_naming_convention" {
          enabled = "yes"
        }
        """,
        "'enabled' must be true or false.")]
    [InlineData(
        """
        rule "terraform_naming_convention" {
          enabled        = true
          custom_formats = ["a"]
        }
        """,
        "'custom_formats' must be a map of objects.")]
    public void ThrowsForAConfigurationTflintRejects(string config, string expectedMessage)
    {
        FormatException exception = Assert.Throws<FormatException>(
            () => TerraformNamingConvention.FromTflintConfig(config));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void ThrowsForAnInvalidPattern()
    {
        FormatException exception = Assert.Throws<FormatException>(
            () => TerraformNamingConvention.FromTflintConfig("""
                rule "terraform_naming_convention" {
                  enabled = true

                  resource {
                    custom = "^[a-z"
                  }
                }
                """));

        Assert.StartsWith("Invalid resource configuration: ", exception.Message);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void ThrowsForInvalidHcl()
    {
        Assert.Throws<HclSyntaxException>(
            () => TerraformNamingConvention.FromTflintConfig("rule \"terraform_naming_convention {"));
    }
}
