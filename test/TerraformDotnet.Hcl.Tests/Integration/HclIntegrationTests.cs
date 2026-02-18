using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Integration;

/// <summary>
/// Integration tests using realistic Terraform patterns.
/// Each fixture: parse → verify AST → emit → re-parse → compare.
/// All HCL is original content, not copied from workspace Terraform modules.
/// </summary>
public sealed class HclIntegrationTests
{
    private static string EmitFile(HclFile file)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        var emitter = new HclFileEmitter(writer);
        emitter.Emit(file);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static HclFile Parse(string hcl)
    {
        return HclFileParser.Parse(Encoding.UTF8.GetBytes(hcl));
    }

    private static void AssertRoundTrip(string hcl)
    {
        HclFile first = Parse(hcl);
        string emitted = EmitFile(first);
        HclFile second = Parse(emitted);
        AssertBodiesEqual(first.Body, second.Body);
    }

    // ── Cloud resource ──────────────────────────────────────────

    [Fact]
    public void CloudResourceDefinition()
    {
        string hcl =
            "resource \"cloud_compute\" \"web\" {\n" +
            "  name          = \"web-server\"\n" +
            "  machine_type  = \"standard-2\"\n" +
            "  region        = var.region\n" +
            "  instance_count = var.enabled ? 3 : 0\n" +
            "}\n";

        HclFile file = Parse(hcl);
        Assert.Single(file.Body.Blocks);

        HclBlock block = file.Body.Blocks[0];
        Assert.Equal("cloud_compute", block.Labels[0]);
        Assert.Equal("web", block.Labels[1]);

        AssertRoundTrip(hcl);
    }

    // ── Database configuration ──────────────────────────────────

    [Fact]
    public void DatabaseConfiguration()
    {
        string hcl =
            "resource \"db_cluster\" \"main\" {\n" +
            "  cluster_name = \"main-db\"\n" +
            "  engine       = \"postgresql\"\n" +
            "  version      = \"15.4\"\n" +
            "  count        = 3\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("db_cluster", block.Labels[0]);
        Assert.Equal(4, block.Body.Attributes.Count);

        AssertRoundTrip(hcl);
    }

    // ── Variable definitions ────────────────────────────────────

    [Fact]
    public void VariableDefinitions()
    {
        string hcl =
            "variable \"name\" {\n" +
            "  type        = string\n" +
            "  description = \"The resource name\"\n" +
            "  default     = \"my-resource\"\n" +
            "}\n" +
            "\n" +
            "variable \"count\" {\n" +
            "  type    = number\n" +
            "  default = 3\n" +
            "}\n" +
            "\n" +
            "variable \"enabled\" {\n" +
            "  type    = bool\n" +
            "  default = true\n" +
            "}\n";

        HclFile file = Parse(hcl);
        Assert.Equal(3, file.Body.Blocks.Count);

        AssertRoundTrip(hcl);
    }

    // ── Locals block ────────────────────────────────────────────

    [Fact]
    public void LocalsBlock()
    {
        string hcl =
            "locals {\n" +
            "  env    = \"production\"\n" +
            "  prefix = \"myapp\"\n" +
            "  name   = \"${local.prefix}-${local.env}\"\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("locals", block.Type);

        AssertRoundTrip(hcl);
    }

    // ── Output definitions ──────────────────────────────────────

    [Fact]
    public void OutputDefinitions()
    {
        string hcl =
            "output \"instance_ip\" {\n" +
            "  value       = cloud_compute.web.public_ip\n" +
            "  description = \"The public IP address\"\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("output", block.Type);
        Assert.Equal("instance_ip", block.Labels[0]);

        AssertRoundTrip(hcl);
    }

    // ── Provider configuration ──────────────────────────────────

    [Fact]
    public void ProviderConfiguration()
    {
        string hcl =
            "provider \"cloud\" {\n" +
            "  region  = \"us-east-1\"\n" +
            "  project = var.project_id\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("provider", block.Type);

        AssertRoundTrip(hcl);
    }

    // ── Data source ─────────────────────────────────────────────

    [Fact]
    public void DataSource()
    {
        string hcl =
            "data \"cloud_image\" \"latest\" {\n" +
            "  name   = \"ubuntu-22.04\"\n" +
            "  latest = true\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("data", block.Type);

        AssertRoundTrip(hcl);
    }

    // ── Module call ─────────────────────────────────────────────

    [Fact]
    public void ModuleCall()
    {
        string hcl =
            "module \"vpc\" {\n" +
            "  source  = \"./modules/network\"\n" +
            "  version = \"1.0.0\"\n" +
            "  cidr    = \"10.0.0.0/16\"\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("module", block.Type);
        Assert.Equal("vpc", block.Labels[0]);

        AssertRoundTrip(hcl);
    }

    // ── Terraform settings ──────────────────────────────────────

    [Fact]
    public void TerraformSettings()
    {
        string hcl =
            "terraform {\n" +
            "  required_version = \">= 1.5.0\"\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("terraform", block.Type);

        AssertRoundTrip(hcl);
    }

    // ── Nested blocks ───────────────────────────────────────────

    [Fact]
    public void NestedBlocksStructure()
    {
        string hcl =
            "resource \"cloud_compute\" \"main\" {\n" +
            "  name = \"server\"\n" +
            "\n" +
            "  network {\n" +
            "    subnet = \"default\"\n" +
            "  }\n" +
            "\n" +
            "  disk {\n" +
            "    size = 100\n" +
            "    type = \"ssd\"\n" +
            "  }\n" +
            "}\n";

        HclFile file = Parse(hcl);
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Single(block.Body.Attributes);
        Assert.Equal(2, block.Body.Blocks.Count);
        Assert.Equal("network", block.Body.Blocks[0].Type);
        Assert.Equal("disk", block.Body.Blocks[1].Type);

        AssertRoundTrip(hcl);
    }

    // ── Expression-heavy fixture ────────────────────────────────

    [Fact]
    public void ExpressionHeavyFixture()
    {
        string hcl =
            "value = max(length(var.list), 10)\n";

        HclFile file = Parse(hcl);
        Assert.Single(file.Body.Attributes);

        AssertRoundTrip(hcl);
    }

    // ── Structural comparison helpers ───────────────────────────

    private static void AssertBodiesEqual(HclBody expected, HclBody actual)
    {
        Assert.Equal(expected.Attributes.Count, actual.Attributes.Count);
        for (int i = 0; i < expected.Attributes.Count; i++)
        {
            Assert.Equal(expected.Attributes[i].Name, actual.Attributes[i].Name);
            AssertExpressionsEqual(expected.Attributes[i].Value, actual.Attributes[i].Value);
        }

        Assert.Equal(expected.Blocks.Count, actual.Blocks.Count);
        for (int i = 0; i < expected.Blocks.Count; i++)
        {
            Assert.Equal(expected.Blocks[i].Type, actual.Blocks[i].Type);
            Assert.Equal(expected.Blocks[i].Labels, actual.Blocks[i].Labels);
            AssertBodiesEqual(expected.Blocks[i].Body, actual.Blocks[i].Body);
        }
    }

    private static void AssertExpressionsEqual(HclExpression expected, HclExpression actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());

        switch (expected)
        {
            case HclLiteralExpression litE:
                var litA = (HclLiteralExpression)actual;
                Assert.Equal(litE.Kind, litA.Kind);
                Assert.Equal(litE.Value, litA.Value);
                break;

            case HclVariableExpression varE:
                Assert.Equal(varE.Name, ((HclVariableExpression)actual).Name);
                break;

            case HclBinaryExpression binE:
                var binA = (HclBinaryExpression)actual;
                Assert.Equal(binE.Operator, binA.Operator);
                AssertExpressionsEqual(binE.Left, binA.Left);
                AssertExpressionsEqual(binE.Right, binA.Right);
                break;

            case HclConditionalExpression condE:
                var condA = (HclConditionalExpression)actual;
                AssertExpressionsEqual(condE.Condition, condA.Condition);
                AssertExpressionsEqual(condE.TrueResult, condA.TrueResult);
                AssertExpressionsEqual(condE.FalseResult, condA.FalseResult);
                break;

            case HclFunctionCallExpression funcE:
                var funcA = (HclFunctionCallExpression)actual;
                Assert.Equal(funcE.Name, funcA.Name);
                Assert.Equal(funcE.Arguments.Count, funcA.Arguments.Count);
                for (int i = 0; i < funcE.Arguments.Count; i++)
                {
                    AssertExpressionsEqual(funcE.Arguments[i], funcA.Arguments[i]);
                }

                break;

            case HclTupleExpression tupE:
                var tupA = (HclTupleExpression)actual;
                Assert.Equal(tupE.Elements.Count, tupA.Elements.Count);
                for (int i = 0; i < tupE.Elements.Count; i++)
                {
                    AssertExpressionsEqual(tupE.Elements[i], tupA.Elements[i]);
                }

                break;

            case HclAttributeAccessExpression accE:
                var accA = (HclAttributeAccessExpression)actual;
                Assert.Equal(accE.Name, accA.Name);
                AssertExpressionsEqual(accE.Source, accA.Source);
                break;

            case HclIndexExpression idxE:
                var idxA = (HclIndexExpression)actual;
                AssertExpressionsEqual(idxE.Collection, idxA.Collection);
                AssertExpressionsEqual(idxE.Index, idxA.Index);
                break;

            case HclSplatExpression splatE:
                var splatA = (HclSplatExpression)actual;
                Assert.Equal(splatE.IsFullSplat, splatA.IsFullSplat);
                AssertExpressionsEqual(splatE.Source, splatA.Source);
                break;

            case HclTemplateWrapExpression twE:
                var twA = (HclTemplateWrapExpression)actual;
                AssertExpressionsEqual(twE.Wrapped, twA.Wrapped);
                break;
        }
    }
}
