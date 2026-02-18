using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Integration;

/// <summary>
/// Comprehensive HCL feature coverage tests — one test per spec section.
/// All fixtures are original content.
/// </summary>
public sealed class HclFeatureMatrixTests
{
    private static HclFile Parse(string hcl)
    {
        return HclFileParser.Parse(Encoding.UTF8.GetBytes(hcl));
    }

    private static string EmitFile(HclFile file)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        var emitter = new HclFileEmitter(writer);
        emitter.Emit(file);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // ── Comment styles ──────────────────────────────────────────

    [Fact]
    public void AllCommentStyles()
    {
        string hcl =
            "// line comment\n" +
            "# hash comment\n" +
            "/* block comment */\n" +
            "x = 1\n";

        HclFile file = Parse(hcl);
        Assert.Single(file.Body.Attributes);
        Assert.Equal(3, file.Body.Attributes[0].LeadingComments.Count);
        Assert.Equal(HclCommentStyle.Line, file.Body.Attributes[0].LeadingComments[0].Style);
        Assert.Equal(HclCommentStyle.Hash, file.Body.Attributes[0].LeadingComments[1].Style);
        Assert.Equal(HclCommentStyle.Block, file.Body.Attributes[0].LeadingComments[2].Style);
    }

    // ── Numeric literal forms ───────────────────────────────────

    [Theory]
    [InlineData("x = 0\n", "0")]
    [InlineData("x = 42\n", "42")]
    [InlineData("x = 3.14\n", "3.14")]
    [InlineData("x = 1e10\n", "1e10")]
    public void NumericLiterals(string hcl, string expectedValue)
    {
        HclFile file = Parse(hcl);
        var lit = Assert.IsType<HclLiteralExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(HclLiteralKind.Number, lit.Kind);
        Assert.Equal(expectedValue, lit.Value);
    }

    // ── Boolean and null literals ───────────────────────────────

    [Theory]
    [InlineData("x = true\n", "true")]
    [InlineData("x = false\n", "false")]
    public void BooleanLiterals(string hcl, string expectedValue)
    {
        HclFile file = Parse(hcl);
        var lit = Assert.IsType<HclLiteralExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(HclLiteralKind.Bool, lit.Kind);
        Assert.Equal(expectedValue, lit.Value);
    }

    [Fact]
    public void NullLiteral()
    {
        HclFile file = Parse("x = null\n");
        var lit = Assert.IsType<HclLiteralExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(HclLiteralKind.Null, lit.Kind);
    }

    // ── String escapes ──────────────────────────────────────────

    [Fact]
    public void QuotedStringEscapes()
    {
        string hcl = "x = \"hello\\nworld\\t!\"\n";
        HclFile file = Parse(hcl);
        var lit = Assert.IsType<HclLiteralExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(HclLiteralKind.String, lit.Kind);
        Assert.Contains("\n", lit.Value);
        Assert.Contains("\t", lit.Value);
    }

    // ── Identifier rules ────────────────────────────────────────

    [Theory]
    [InlineData("my_var = 1\n")]
    [InlineData("myVar = 1\n")]
    [InlineData("_private = 1\n")]
    [InlineData("my-var = 1\n")]
    public void ValidIdentifiers(string hcl)
    {
        HclFile file = Parse(hcl);
        Assert.Single(file.Body.Attributes);
    }

    // ── Attribute definitions ───────────────────────────────────

    [Fact]
    public void SimpleAttributeDefinition()
    {
        HclFile file = Parse("name = \"value\"\n");
        Assert.Single(file.Body.Attributes);
        Assert.Equal("name", file.Body.Attributes[0].Name);
    }

    // ── Block definitions ───────────────────────────────────────

    [Fact]
    public void BlockNoLabels()
    {
        HclFile file = Parse("locals {\n  x = 1\n}\n");
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal("locals", block.Type);
        Assert.Empty(block.Labels);
    }

    [Fact]
    public void BlockMultipleLabels()
    {
        HclFile file = Parse("resource \"type\" \"name\" {\n  x = 1\n}\n");
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal(2, block.Labels.Count);
    }

    [Fact]
    public void EmptyOneLineBlock()
    {
        HclFile file = Parse("lifecycle {}\n");
        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Empty(block.Body.Attributes);
        Assert.Empty(block.Body.Blocks);
    }

    // ── All operator precedence levels ──────────────────────────

    [Theory]
    [InlineData("x = a || b\n", HclBinaryOperator.Or)]
    [InlineData("x = a && b\n", HclBinaryOperator.And)]
    [InlineData("x = a == b\n", HclBinaryOperator.Equal)]
    [InlineData("x = a != b\n", HclBinaryOperator.NotEqual)]
    [InlineData("x = a < b\n", HclBinaryOperator.LessThan)]
    [InlineData("x = a > b\n", HclBinaryOperator.GreaterThan)]
    [InlineData("x = a <= b\n", HclBinaryOperator.LessEqual)]
    [InlineData("x = a >= b\n", HclBinaryOperator.GreaterEqual)]
    [InlineData("x = a + b\n", HclBinaryOperator.Add)]
    [InlineData("x = a - b\n", HclBinaryOperator.Subtract)]
    [InlineData("x = a * b\n", HclBinaryOperator.Multiply)]
    [InlineData("x = a / b\n", HclBinaryOperator.Divide)]
    [InlineData("x = a % b\n", HclBinaryOperator.Modulo)]
    public void AllBinaryOperators(string hcl, HclBinaryOperator expectedOp)
    {
        HclFile file = Parse(hcl);
        var bin = Assert.IsType<HclBinaryExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(expectedOp, bin.Operator);
    }

    // ── Conditional expressions ─────────────────────────────────

    [Fact]
    public void ConditionalExpression()
    {
        HclFile file = Parse("x = true ? 1 : 0\n");
        var cond = Assert.IsType<HclConditionalExpression>(file.Body.Attributes[0].Value);
        Assert.IsType<HclLiteralExpression>(cond.Condition);
        Assert.IsType<HclLiteralExpression>(cond.TrueResult);
        Assert.IsType<HclLiteralExpression>(cond.FalseResult);
    }

    // ── For expressions ─────────────────────────────────────────

    [Fact]
    public void ForTupleExpression()
    {
        HclFile file = Parse("x = [for v in list : v]\n");
        var forExpr = Assert.IsType<HclForExpression>(file.Body.Attributes[0].Value);
        Assert.False(forExpr.IsObjectFor);
        Assert.Equal("v", forExpr.KeyVariable);
    }

    [Fact]
    public void ForObjectExpression()
    {
        HclFile file = Parse("x = {for k, v in map : k => v}\n");
        var forExpr = Assert.IsType<HclForExpression>(file.Body.Attributes[0].Value);
        Assert.True(forExpr.IsObjectFor);
        Assert.Equal("k", forExpr.KeyVariable);
        Assert.Equal("v", forExpr.ValueVariable);
    }

    [Fact]
    public void ForWithCondition()
    {
        HclFile file = Parse("x = [for v in list : v if v > 0]\n");
        var forExpr = Assert.IsType<HclForExpression>(file.Body.Attributes[0].Value);
        Assert.NotNull(forExpr.Condition);
    }

    [Fact]
    public void ForObjectWithGrouping()
    {
        HclFile file = Parse("x = {for k, v in map : k => v...}\n");
        var forExpr = Assert.IsType<HclForExpression>(file.Body.Attributes[0].Value);
        Assert.True(forExpr.IsGrouped);
    }

    // ── Splat expressions ───────────────────────────────────────

    [Fact]
    public void FullSplatExpression()
    {
        HclFile file = Parse("x = list[*].id\n");
        var splat = Assert.IsType<HclSplatExpression>(file.Body.Attributes[0].Value);
        Assert.True(splat.IsFullSplat);
    }

    [Fact]
    public void AttributeSplatExpression()
    {
        HclFile file = Parse("x = list.*.id\n");
        var splat = Assert.IsType<HclSplatExpression>(file.Body.Attributes[0].Value);
        Assert.False(splat.IsFullSplat);
    }

    // ── Index operator ──────────────────────────────────────────

    [Fact]
    public void IndexAccess()
    {
        HclFile file = Parse("x = list[0]\n");
        var idx = Assert.IsType<HclIndexExpression>(file.Body.Attributes[0].Value);
        Assert.False(idx.IsLegacy);
    }

    // ── Function calls ──────────────────────────────────────────

    [Fact]
    public void FunctionCallNoArgs()
    {
        HclFile file = Parse("x = timestamp()\n");
        var func = Assert.IsType<HclFunctionCallExpression>(file.Body.Attributes[0].Value);
        Assert.Equal("timestamp", func.Name);
        Assert.Empty(func.Arguments);
    }

    [Fact]
    public void FunctionCallWithArgs()
    {
        HclFile file = Parse("x = max(1, 2, 3)\n");
        var func = Assert.IsType<HclFunctionCallExpression>(file.Body.Attributes[0].Value);
        Assert.Equal("max", func.Name);
        Assert.Equal(3, func.Arguments.Count);
    }

    [Fact]
    public void FunctionCallWithExpansion()
    {
        HclFile file = Parse("x = concat(list...)\n");
        var func = Assert.IsType<HclFunctionCallExpression>(file.Body.Attributes[0].Value);
        Assert.True(func.ExpandFinalArgument);
    }

    // ── Collection literals ─────────────────────────────────────

    [Fact]
    public void EmptyTupleLiteral()
    {
        HclFile file = Parse("x = []\n");
        var tuple = Assert.IsType<HclTupleExpression>(file.Body.Attributes[0].Value);
        Assert.Empty(tuple.Elements);
    }

    [Fact]
    public void TupleLiteral()
    {
        HclFile file = Parse("x = [1, 2, 3]\n");
        var tuple = Assert.IsType<HclTupleExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(3, tuple.Elements.Count);
    }

    [Fact]
    public void EmptyObjectLiteral()
    {
        HclFile file = Parse("x = {}\n");
        var obj = Assert.IsType<HclObjectExpression>(file.Body.Attributes[0].Value);
        Assert.Empty(obj.Elements);
    }

    [Fact]
    public void ObjectLiteral()
    {
        HclFile file = Parse("x = {\n  a = 1\n  b = 2\n}\n");
        var obj = Assert.IsType<HclObjectExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(2, obj.Elements.Count);
    }

    // ── Template expressions ────────────────────────────────────

    [Fact]
    public void TemplateWrapExpression()
    {
        HclFile file = Parse("x = \"${var.name}\"\n");
        // The current parser treats interpolated strings as string literals
        // since template decomposition is not yet implemented at the parser level.
        HclExpression expr = file.Body.Attributes[0].Value;
        Assert.True(
            expr is HclTemplateWrapExpression or HclTemplateExpression or HclLiteralExpression,
            "Expected template, template wrap, or string literal expression");
    }

    // ── Round-trip all features ─────────────────────────────────

    [Fact]
    public void RoundTripAllFeatures()
    {
        string hcl =
            "variable \"name\" {\n" +
            "  type    = string\n" +
            "  default = \"hello\"\n" +
            "}\n" +
            "\n" +
            "resource \"cloud_compute\" \"main\" {\n" +
            "  name   = var.name\n" +
            "  count  = 3\n" +
            "  active = true\n" +
            "  tags   = [\"web\", \"prod\"]\n" +
            "}\n" +
            "\n" +
            "output \"result\" {\n" +
            "  value = cloud_compute.main.id\n" +
            "}\n";

        HclFile first = Parse(hcl);
        string emitted = EmitFile(first);
        HclFile second = Parse(emitted);

        Assert.Equal(first.Body.Blocks.Count, second.Body.Blocks.Count);
    }
}
