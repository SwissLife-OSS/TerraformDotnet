using System.Text;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Tests.Common;
using TerraformDotnet.Hcl.Tests.RoundTrip;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Writer;

public sealed class HclFormatterTests
{
    /// <summary>
    /// Inputs with wrong indentation and alignment, but with the spacing inside each line already
    /// canonical (the formatter does not change it).
    /// </summary>
    public static TheoryData<string, string> MessyCases => new()
    {
        {
            "block attributes",
            """
            resource "aws_instance" "web" {
            ami = "abc"
                instance_type     = "t2.micro"
              count = 2
            }

            """
        },
        {
            "nested object",
            """
            locals {
            tags = {
            Name = "web"
                  Environment = "dev"
            }
            region = "eu"
            }

            """
        },
        {
            "blank line splits alignment",
            """
            locals {
            a = 1
            bbbb = 2

            cc = 3
            d = 4
            }

            """
        },
        {
            "conditional between objects",
            """
            locals {
            x = var.a ? {
            a = 1
            } : {
            bb = 2
            }
            y = 1
            }

            """
        },
        {
            "list for with condition",
            """
            locals {
            y = [
            for v in var.list : v
            if v != ""
            ]
            z = 1
            }

            """
        },
        {
            "object for",
            """
            locals {
            m = {
            for k, v in var.map : k => v
            }
            }

            """
        },
        {
            "function call with multi-line arguments",
            """
            locals {
            z = merge(
            var.a,
            {
            k = "v"
            long_key = "w"
            },
            )
            }

            """
        },
        {
            "hugged function call",
            """
            locals {
            z = jsonencode({
            a = 1
            bb = [
            1,
            2,
            ]
            })
            after = true
            }

            """
        },
        {
            "several closers on one line",
            """
            locals {
            a = [{
            b = 1
            }]
            c = 2
            d = [[
            1,
            ]]
            }

            """
        },
        {
            "binary operator between multi-line operands",
            """
            locals {
            a = (
            1 +
            2
            )
            b = [
            1,
            ] == [
            2,
            ]
            }

            """
        },
        {
            "heredoc",
            """
            locals {
            h = <<EOT
              keep
                this
            EOT
            other = 1
            o = 22
            }

            """
        },
        {
            "indented heredoc in call",
            """
            locals {
            first = 1
            h = trimspace(<<-EOT
                keep
              this
              EOT
            )
            other = 1
            }

            """
        },
        {
            "heredoc in object",
            """
            locals {
            o = {
            a = <<EOT
            body { not a bracket
            EOT
            bb = 1
            }
            }

            """
        },
        {
            "trailing comments",
            """
            variable "x" {
            type = string # the type
            default = "abc" // default
            # own line
            description = "d"
            nullable = false # x
            }

            """
        },
        {
            "trailing comments without assignments",
            """
            locals {
            list = [
            1, # one
            200, # two hundred
            3,
            ]
            }

            """
        },
        {
            "block comments",
            """
            /* a
               b */
            locals {
            /* inline */
            a = 1 /* after */
            bbb = 2
              /* multi
                 line */
            c = 3
            }

            """
        },
        {
            "nested blocks",
            """
            resource "a" "b" {
            name = "n"
            lifecycle {
            ignore_changes = [
            tags,
            ]
            }
            dynamic "setting" {
            for_each = var.settings
            content {
            name = setting.key
            value = setting.value
            }
            }
            }

            """
        },
        {
            "non-ASCII keys",
            """
            locals {
            m = {
            "äöü" = 1
            "abcd" = 2
            "日本" = 3
            }
            }

            """
        },
        {
            "one-line blocks and empty bodies",
            """
            locals {}
            module "m" {
            source = "./m"
            providers = { aws = aws.east }
            }

            """
        },
        {
            "assignment followed by comment-only lines",
            """
            locals {
                # leading
            a = 1
            # between
            bbbb = 2
            }

            """
        },
        {
            "padded assignment that opens brackets",
            """
            x = {
            a          = 1
            dimensions          = optional(list(object({
            name = string
            })), [])
            b       = [
            1,
            ]
            }

            """
        },
    };

    [Theory]
    [MemberData(nameof(MessyCases))]
    public void Format_FormattedText_IsUnchanged(string name, string hcl)
    {
        _ = name;
        string formatted = HclFormatter.Format(hcl);

        Assert.Equal(formatted, HclFormatter.Format(formatted));
    }

    [Theory]
    [MemberData(nameof(MessyCases))]
    public void Format_Bytes_MatchesString(string name, string hcl)
    {
        _ = name;

        byte[] formatted = HclFormatter.Format(Encoding.UTF8.GetBytes(hcl));

        Assert.Equal(HclFormatter.Format(hcl), Encoding.UTF8.GetString(formatted));
    }

    [Theory]
    [MemberData(nameof(HclFmtStabilityTests.CanonicalCases), MemberType = typeof(HclFmtStabilityTests))]
    public void Format_CanonicalText_IsUnchanged(string name, string hcl)
    {
        _ = name;

        Assert.Equal(hcl, HclFormatter.Format(hcl));
    }

    [Fact]
    public void Format_ConditionalBetweenObjects_IndentsLikeTerraformFmt()
    {
        const string input = """
            x = a ? {
            k = 1
            } : {
            k = 2
            }

            """;

        Assert.Equal(
            """
            x = a ? {
              k = 1
              } : {
              k = 2
            }

            """,
            HclFormatter.Format(input));
    }

    [Fact]
    public void Format_AlignsAssignmentsAndComments()
    {
        const string input = """
            a = 1 # one
            bbb = "three" # three

            """;

        Assert.Equal(
            """
            a   = 1       # one
            bbb = "three" # three

            """,
            HclFormatter.Format(input));
    }

    [Fact]
    public void Format_OpeningLine_IsNotAligned()
    {
        const string input = """
            a = 1
            bbbbbb = {
            c = 1
            }
            dd = 2

            """;

        Assert.Equal(
            """
            a = 1
            bbbbbb = {
              c = 1
            }
            dd = 2

            """,
            HclFormatter.Format(input));
    }

    [Fact]
    public void Format_HeredocBody_IsKeptAndAlignmentContinues()
    {
        const string input = "a = 1\nbb = <<EOT\n  body\n\tEOT text\nEOT\nccc = 2\n";

        Assert.Equal(
            "a   = 1\nbb  = <<EOT\n  body\n\tEOT text\nEOT\nccc = 2\n",
            HclFormatter.Format(input));
    }

    [Fact]
    public void Format_CrLf_IsKept()
    {
        const string input = "locals {\r\na = 1\r\nbbb = 2\r\n}\r\n";

        Assert.Equal("locals {\r\n  a   = 1\r\n  bbb = 2\r\n}\r\n", HclFormatter.Format(input));
    }

    [Fact]
    public void Format_TrailingWhitespace_IsRemoved()
    {
        const string input = "locals {   \n  a = 1 \t\n\n   \n}";

        Assert.Equal("locals {\n  a = 1\n\n\n}", HclFormatter.Format(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("# only a comment\n")]
    public void Format_TrivialInput_IsUnchanged(string hcl)
    {
        Assert.Equal(hcl, HclFormatter.Format(hcl));
    }

    [Fact]
    public void Format_WithBaseLevel_IndentsEveryLineButHeredocBodies()
    {
        const string input = "a = 1\nbb = {\nc = <<EOT\nbody\nEOT\n}\n";
        var output = new System.Buffers.ArrayBufferWriter<byte>();

        HclFormatter.Format(Encoding.UTF8.GetBytes(input), 2, output);

        Assert.Equal(
            "    a = 1\n    bb = {\n      c = <<EOT\nbody\nEOT\n    }\n",
            Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Theory]
    [InlineData("a = ")]
    [InlineData("locals {\n  a = 1\n")]
    [InlineData("a = [1, 2\n")]
    public void Format_InvalidHcl_Throws(string hcl)
    {
        Assert.ThrowsAny<HclSyntaxException>(() => HclFormatter.Format(hcl));
    }

    [Fact]
    public void Format_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HclFormatter.Format((string)null!));
    }

    /// <summary>Checks against the real <c>terraform fmt</c> running in a container.</summary>
    [Collection(TerraformFmtCollection.Name)]
    public sealed class AgainstTerraformFmt(TerraformFmtResource terraform)
    {
        [Theory]
        [MemberData(nameof(MessyCases), MemberType = typeof(HclFormatterTests))]
        public async Task Format_MessyInput_MatchesTerraformFmt(string name, string hcl)
        {
            _ = name;

            Assert.Equal(await terraform.FormatAsync(hcl), HclFormatter.Format(hcl));
        }
    }
}
