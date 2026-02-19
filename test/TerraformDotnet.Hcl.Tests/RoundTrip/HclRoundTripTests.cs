using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.RoundTrip;

/// <summary>
/// Tests that Parse → Emit → Re-parse produces structurally identical ASTs
/// and that output conforms to <c>terraform fmt</c> canonical style.
/// </summary>
public sealed class HclRoundTripTests
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

    private static void AssertRoundTrip(string input)
    {
        HclFile first = Parse(input);
        string emitted = EmitFile(first);
        HclFile second = Parse(emitted);

        // Assert structural equality
        AssertBodiesEqual(first.Body, second.Body);
    }

    private static void AssertIdempotent(string canonical)
    {
        HclFile file = Parse(canonical);
        string output = EmitFile(file);
        Assert.Equal(canonical, output);
    }

    // ── Simple attribute tests ──────────────────────────────────

    [Fact]
    public void SimpleAttributes()
    {
        AssertRoundTrip("name = \"hello\"\ncount = 42\n");
    }

    [Fact]
    public void SimpleAttributesIdempotent()
    {
        AssertIdempotent("name  = \"hello\"\ncount = 42\n");
    }

    // ── Block tests ─────────────────────────────────────────────

    [Fact]
    public void EmptyBlock()
    {
        AssertRoundTrip("resource \"test\" \"main\" {}\n");
    }

    [Fact]
    public void BlockWithAttributes()
    {
        string input =
            "resource \"aws_instance\" \"main\" {\n" +
            "  ami           = \"abc-123\"\n" +
            "  instance_type = \"t2.micro\"\n" +
            "}\n";

        AssertIdempotent(input);
    }

    [Fact]
    public void NestedBlocks()
    {
        string input =
            "resource \"aws_instance\" \"main\" {\n" +
            "  ami = \"abc-123\"\n" +
            "\n" +
            "  lifecycle {\n" +
            "    create_before_destroy = true\n" +
            "  }\n" +
            "}\n";

        AssertRoundTrip(input);
    }

    [Fact]
    public void DeeplyNestedBlocks()
    {
        string input =
            "a {\n" +
            "  b {\n" +
            "    c {\n" +
            "      x = 1\n" +
            "    }\n" +
            "  }\n" +
            "}\n";

        AssertIdempotent(input);
    }

    // ── Expression roundtrips ───────────────────────────────────

    [Fact]
    public void AllExpressionTypes()
    {
        string input =
            "s = \"hello\"\n" +
            "n = 42\n" +
            "b = true\n" +
            "x = null\n" +
            "v = var.name\n";

        AssertRoundTrip(input);
    }

    [Fact]
    public void BinaryExpression()
    {
        AssertRoundTrip("x = 1 + 2 * 3\n");
    }

    [Fact]
    public void ConditionalExpression()
    {
        AssertRoundTrip("x = enabled ? \"yes\" : \"no\"\n");
    }

    [Fact]
    public void FunctionCall()
    {
        AssertRoundTrip("x = max(1, 2, 3)\n");
    }

    [Fact]
    public void TupleExpression()
    {
        AssertRoundTrip("x = [1, 2, 3]\n");
    }

    [Fact]
    public void ForExpression()
    {
        AssertRoundTrip("ids = [for v in list : v.id]\n");
    }

    [Fact]
    public void SplatExpression()
    {
        AssertRoundTrip("ids = instances[*].id\n");
    }

    // ── Comment roundtrips ──────────────────────────────────────

    [Fact]
    public void CommentsPreserved()
    {
        string input =
            "// top comment\n" +
            "name = \"hello\"\n";

        AssertRoundTrip(input);
    }

    [Fact]
    public void HashCommentPreserved()
    {
        string input =
            "# hash comment\n" +
            "name = \"hello\"\n";

        AssertRoundTrip(input);
    }

    // ── Messy formatting normalization ──────────────────────────

    [Fact]
    public void MessyFormattingNormalized()
    {
        string messy = "x   =   1\n";
        HclFile file = Parse(messy);
        string emitted = EmitFile(file);
        Assert.Equal("x = 1\n", emitted);
    }

    // ── Full terraform file ─────────────────────────────────────

    [Fact]
    public void FullTerraformFile()
    {
        string input =
            "variable \"name\" {\n" +
            "  type    = string\n" +
            "  default = \"hello\"\n" +
            "}\n" +
            "\n" +
            "resource \"aws_instance\" \"main\" {\n" +
            "  ami           = \"abc-123\"\n" +
            "  instance_type = \"t2.micro\"\n" +
            "}\n";

        AssertRoundTrip(input);
    }

    // ── Blank line preservation & per-group alignment ───────────

    [Fact]
    public void BlankLineBetweenAttributes_Preserved()
    {
        string input =
            "resource \"test\" \"main\" {\n" +
            "  source = \"./modules/test\"\n" +
            "\n" +
            "  name = \"foo\"\n" +
            "}\n";

        AssertIdempotent(input);
    }

    [Fact]
    public void BlankLineBetweenAttributes_HasLeadingBlankLineFlag()
    {
        string input =
            "resource \"test\" \"main\" {\n" +
            "  first = 1\n" +
            "\n" +
            "  second = 2\n" +
            "}\n";

        HclFile file = Parse(input);
        var block = file.Body.Blocks[0];
        var attrs = block.Body.Attributes;

        Assert.False(attrs[0].HasLeadingBlankLine);
        Assert.True(attrs[1].HasLeadingBlankLine);
    }

    [Fact]
    public void PerGroupAlignment_IndependentGroups()
    {
        // terraform fmt aligns = per group separated by blank lines
        string input =
            "resource \"test\" \"main\" {\n" +
            "  source = \"./modules/test\"\n" +
            "\n" +
            "  department  = var.department\n" +
            "  environment = var.environment\n" +
            "  tags         = local.tags\n" +
            "}\n";

        // After parse → emit, each group should be aligned independently
        HclFile file = Parse(input);
        string output = EmitFile(file);

        // Group 1: "source" alone → no padding
        Assert.Contains("  source = \"./modules/test\"", output);
        // Group 2: "department", "environment", "tags" → aligned to "environment"
        Assert.Contains("  department  = var.department", output);
        Assert.Contains("  environment = var.environment", output);
        Assert.Contains("  tags        = local.tags", output);
    }

    [Fact]
    public void PerGroupAlignment_NoBlankLine_GlobalAlignment()
    {
        // Without blank lines, all attributes form one group
        string input =
            "resource \"test\" \"main\" {\n" +
            "  source = \"./modules/test\"\n" +
            "  environment = var.environment\n" +
            "}\n";

        HclFile file = Parse(input);
        string output = EmitFile(file);

        // Both attributes aligned to "environment"
        Assert.Contains("  source      = \"./modules/test\"", output);
        Assert.Contains("  environment = var.environment", output);
    }

    [Fact]
    public void PerGroupAlignment_BlockBreaksGroup()
    {
        // A nested block between attributes breaks alignment groups
        string input =
            "resource \"test\" \"main\" {\n" +
            "  short = 1\n" +
            "  longer_name = 2\n" +
            "\n" +
            "  lifecycle {\n" +
            "    create_before_destroy = true\n" +
            "  }\n" +
            "\n" +
            "  x = 3\n" +
            "}\n";

        HclFile file = Parse(input);
        string output = EmitFile(file);

        // Group 1: "short", "longer_name" aligned together
        Assert.Contains("  short       = 1", output);
        Assert.Contains("  longer_name = 2", output);
        // Group 2: "x" alone → no padding
        Assert.Contains("  x = 3", output);
    }

    [Fact]
    public void BlankLineBetweenTopLevelAttributes_Preserved()
    {
        string input =
            "name = \"hello\"\n" +
            "\n" +
            "count = 42\n";

        AssertIdempotent(input);
    }

    [Fact]
    public void MultipleBlankLines_CollapsedToOne()
    {
        // Multiple blank lines between attributes → one blank line in output
        string input =
            "resource \"test\" \"main\" {\n" +
            "  first = 1\n" +
            "\n" +
            "\n" +
            "\n" +
            "  second = 2\n" +
            "}\n";

        HclFile file = Parse(input);
        string output = EmitFile(file);

        // Should have exactly one blank line between them
        Assert.Contains("  first = 1\n\n  second = 2", output);
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

            case HclUnaryExpression unE:
                var unA = (HclUnaryExpression)actual;
                Assert.Equal(unE.Operator, unA.Operator);
                AssertExpressionsEqual(unE.Operand, unA.Operand);
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

                Assert.Equal(funcE.ExpandFinalArgument, funcA.ExpandFinalArgument);
                break;

            case HclTupleExpression tupE:
                var tupA = (HclTupleExpression)actual;
                Assert.Equal(tupE.Elements.Count, tupA.Elements.Count);
                for (int i = 0; i < tupE.Elements.Count; i++)
                {
                    AssertExpressionsEqual(tupE.Elements[i], tupA.Elements[i]);
                }

                break;

            case HclObjectExpression objE:
                var objA = (HclObjectExpression)actual;
                Assert.Equal(objE.Elements.Count, objA.Elements.Count);
                for (int i = 0; i < objE.Elements.Count; i++)
                {
                    AssertExpressionsEqual(objE.Elements[i].Key, objA.Elements[i].Key);
                    AssertExpressionsEqual(objE.Elements[i].Value, objA.Elements[i].Value);
                }

                break;

            case HclForExpression forE:
                var forA = (HclForExpression)actual;
                Assert.Equal(forE.KeyVariable, forA.KeyVariable);
                Assert.Equal(forE.ValueVariable, forA.ValueVariable);
                Assert.Equal(forE.IsObjectFor, forA.IsObjectFor);
                Assert.Equal(forE.IsGrouped, forA.IsGrouped);
                AssertExpressionsEqual(forE.Collection, forA.Collection);
                AssertExpressionsEqual(forE.ValueExpression, forA.ValueExpression);
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
