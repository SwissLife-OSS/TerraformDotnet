using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Nodes;

public sealed class HclFileParserTests
{
    private static HclFile Parse(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return HclFile.Load(bytes);
    }

    // ── Simple Attributes ───────────────────────────────────

    [Fact]
    public void ParseSimpleStringAttribute()
    {
        var file = Parse("name = \"hello\"\n");

        Assert.Single(file.Body.Attributes);
        Assert.Empty(file.Body.Blocks);

        var attr = file.Body.Attributes[0];
        Assert.Equal("name", attr.Name);

        var lit = Assert.IsType<HclLiteralExpression>(attr.Value);
        Assert.Equal("hello", lit.Value);
        Assert.Equal(HclLiteralKind.String, lit.Kind);
    }

    [Fact]
    public void ParseNumberAttribute()
    {
        var file = Parse("count = 42\n");

        var attr = Assert.Single(file.Body.Attributes);
        Assert.Equal("count", attr.Name);

        var lit = Assert.IsType<HclLiteralExpression>(attr.Value);
        Assert.Equal("42", lit.Value);
        Assert.Equal(HclLiteralKind.Number, lit.Kind);
    }

    [Fact]
    public void ParseBoolAttribute()
    {
        var file = Parse("enabled = true\n");

        var attr = Assert.Single(file.Body.Attributes);
        var lit = Assert.IsType<HclLiteralExpression>(attr.Value);
        Assert.Equal("true", lit.Value);
        Assert.Equal(HclLiteralKind.Bool, lit.Kind);
    }

    [Fact]
    public void ParseNullAttribute()
    {
        var file = Parse("value = null\n");

        var attr = Assert.Single(file.Body.Attributes);
        var lit = Assert.IsType<HclLiteralExpression>(attr.Value);
        Assert.Null(lit.Value);
        Assert.Equal(HclLiteralKind.Null, lit.Kind);
    }

    [Fact]
    public void ParseVariableAttribute()
    {
        var file = Parse("source = var\n");

        var attr = Assert.Single(file.Body.Attributes);
        var varExpr = Assert.IsType<HclVariableExpression>(attr.Value);
        Assert.Equal("var", varExpr.Name);
    }

    [Fact]
    public void ParseMultipleAttributes()
    {
        var file = Parse("a = 1\nb = 2\nc = 3\n");

        Assert.Equal(3, file.Body.Attributes.Count);
        Assert.Equal("a", file.Body.Attributes[0].Name);
        Assert.Equal("b", file.Body.Attributes[1].Name);
        Assert.Equal("c", file.Body.Attributes[2].Name);
    }

    [Fact]
    public void DuplicateAttributeThrows()
    {
        var ex = Assert.Throws<HclSemanticException>(() => Parse("x = 1\nx = 2\n"));
        Assert.Contains("Duplicate attribute 'x'", ex.Message);
    }

    // ── Blocks ──────────────────────────────────────────────

    [Fact]
    public void ParseBlockNoLabels()
    {
        var file = Parse("locals {\n  x = 1\n}\n");

        Assert.Empty(file.Body.Attributes);
        var block = Assert.Single(file.Body.Blocks);
        Assert.Equal("locals", block.Type);
        Assert.Empty(block.Labels);
        Assert.Single(block.Body.Attributes);
        Assert.Equal("x", block.Body.Attributes[0].Name);
    }

    [Fact]
    public void ParseBlockWithLabels()
    {
        var file = Parse("resource \"aws_instance\" \"web\" {\n  ami = \"abc\"\n}\n");

        var block = Assert.Single(file.Body.Blocks);
        Assert.Equal("resource", block.Type);
        Assert.Equal(2, block.Labels.Count);
        Assert.Equal("aws_instance", block.Labels[0]);
        Assert.Equal("web", block.Labels[1]);

        var attr = Assert.Single(block.Body.Attributes);
        Assert.Equal("ami", attr.Name);
        var lit = Assert.IsType<HclLiteralExpression>(attr.Value);
        Assert.Equal("abc", lit.Value);
    }

    [Fact]
    public void ParseEmptyBlock()
    {
        var file = Parse("lifecycle {}\n");

        var block = Assert.Single(file.Body.Blocks);
        Assert.Equal("lifecycle", block.Type);
        Assert.Empty(block.Body.Attributes);
        Assert.Empty(block.Body.Blocks);
    }

    [Fact]
    public void ParseNestedBlocks()
    {
        var hcl = """
            resource "aws_instance" "main" {
              provisioner "local-exec" {
                command = "echo hello"
              }
            }
            """;
        var file = Parse(hcl);

        var outer = Assert.Single(file.Body.Blocks);
        Assert.Equal("resource", outer.Type);

        var inner = Assert.Single(outer.Body.Blocks);
        Assert.Equal("provisioner", inner.Type);
        Assert.Single(inner.Labels);
        Assert.Equal("local-exec", inner.Labels[0]);

        var attr = Assert.Single(inner.Body.Attributes);
        Assert.Equal("command", attr.Name);
    }

    [Fact]
    public void ParseMultipleBlocks()
    {
        var hcl = """
            variable "name" {
              default = "world"
            }
            variable "count" {
              default = 1
            }
            """;
        var file = Parse(hcl);

        Assert.Equal(2, file.Body.Blocks.Count);
        Assert.Equal("name", file.Body.Blocks[0].Labels[0]);
        Assert.Equal("count", file.Body.Blocks[1].Labels[0]);
    }

    [Fact]
    public void ParseMixedAttributesAndBlocks()
    {
        var hcl = """
            provider = "aws"
            resource "type" "name" {
              attr = true
            }
            """;
        var file = Parse(hcl);

        Assert.Single(file.Body.Attributes);
        Assert.Single(file.Body.Blocks);
    }

    // ── Binary Expressions ──────────────────────────────────

    [Fact]
    public void ParseAddition()
    {
        var file = Parse("x = a + b\n");

        var attr = Assert.Single(file.Body.Attributes);
        var bin = Assert.IsType<HclBinaryExpression>(attr.Value);
        Assert.Equal(HclBinaryOperator.Add, bin.Operator);

        var left = Assert.IsType<HclVariableExpression>(bin.Left);
        Assert.Equal("a", left.Name);

        var right = Assert.IsType<HclVariableExpression>(bin.Right);
        Assert.Equal("b", right.Name);
    }

    [Fact]
    public void ParsePrecedence_MultiplyBeforeAdd()
    {
        var file = Parse("x = a + b * c\n");

        var attr = Assert.Single(file.Body.Attributes);
        var add = Assert.IsType<HclBinaryExpression>(attr.Value);
        Assert.Equal(HclBinaryOperator.Add, add.Operator);

        Assert.IsType<HclVariableExpression>(add.Left);

        var mul = Assert.IsType<HclBinaryExpression>(add.Right);
        Assert.Equal(HclBinaryOperator.Multiply, mul.Operator);
    }

    [Fact]
    public void ParseAllArithmetic()
    {
        // Tests: a - b / c % d
        // Expected: a - ((b / c) % d) — actually / and % have same precedence, left-to-right
        // So: (a) - ((b / c) % d)
        var file = Parse("x = a - b / c % d\n");

        var attr = Assert.Single(file.Body.Attributes);
        var sub = Assert.IsType<HclBinaryExpression>(attr.Value);
        Assert.Equal(HclBinaryOperator.Subtract, sub.Operator);

        var mod = Assert.IsType<HclBinaryExpression>(sub.Right);
        Assert.Equal(HclBinaryOperator.Modulo, mod.Operator);

        var div = Assert.IsType<HclBinaryExpression>(mod.Left);
        Assert.Equal(HclBinaryOperator.Divide, div.Operator);
    }

    [Fact]
    public void ParseComparison()
    {
        var file = Parse("x = a > b\n");
        var bin = Assert.IsType<HclBinaryExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(HclBinaryOperator.GreaterThan, bin.Operator);
    }

    [Fact]
    public void ParseEquality()
    {
        var file = Parse("x = a == b\n");
        var bin = Assert.IsType<HclBinaryExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(HclBinaryOperator.Equal, bin.Operator);
    }

    [Fact]
    public void ParseLogicalOr()
    {
        var file = Parse("x = a || b\n");
        var bin = Assert.IsType<HclBinaryExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(HclBinaryOperator.Or, bin.Operator);
    }

    [Fact]
    public void ParseLogicalAnd()
    {
        var file = Parse("x = a && b\n");
        var bin = Assert.IsType<HclBinaryExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(HclBinaryOperator.And, bin.Operator);
    }

    [Fact]
    public void ParseFullPrecedenceChain()
    {
        // !a || b && c == d + e * f
        // Precedence: ! > * > + > == > && > || > ?
        // Expected: (!a) || (b && (c == (d + (e * f))))
        var file = Parse("x = !a || b && c == d + e * f\n");
        var attr = Assert.Single(file.Body.Attributes);

        var or = Assert.IsType<HclBinaryExpression>(attr.Value);
        Assert.Equal(HclBinaryOperator.Or, or.Operator);

        var notA = Assert.IsType<HclUnaryExpression>(or.Left);
        Assert.Equal(HclUnaryOperator.Not, notA.Operator);

        var and = Assert.IsType<HclBinaryExpression>(or.Right);
        Assert.Equal(HclBinaryOperator.And, and.Operator);

        var eq = Assert.IsType<HclBinaryExpression>(and.Right);
        Assert.Equal(HclBinaryOperator.Equal, eq.Operator);

        var add = Assert.IsType<HclBinaryExpression>(eq.Right);
        Assert.Equal(HclBinaryOperator.Add, add.Operator);

        var mul = Assert.IsType<HclBinaryExpression>(add.Right);
        Assert.Equal(HclBinaryOperator.Multiply, mul.Operator);
    }

    // ── Unary Expressions ───────────────────────────────────

    [Fact]
    public void ParseUnaryMinus()
    {
        var file = Parse("x = -42\n");
        var attr = Assert.Single(file.Body.Attributes);
        var un = Assert.IsType<HclUnaryExpression>(attr.Value);
        Assert.Equal(HclUnaryOperator.Negate, un.Operator);
        var lit = Assert.IsType<HclLiteralExpression>(un.Operand);
        Assert.Equal("42", lit.Value);
    }

    [Fact]
    public void ParseUnaryNot()
    {
        var file = Parse("x = !enabled\n");
        var attr = Assert.Single(file.Body.Attributes);
        var un = Assert.IsType<HclUnaryExpression>(attr.Value);
        Assert.Equal(HclUnaryOperator.Not, un.Operator);
        Assert.IsType<HclVariableExpression>(un.Operand);
    }

    // ── Conditional Expressions ─────────────────────────────

    [Fact]
    public void ParseConditional()
    {
        var file = Parse("x = cond ? \"yes\" : \"no\"\n");
        var attr = Assert.Single(file.Body.Attributes);
        var cond = Assert.IsType<HclConditionalExpression>(attr.Value);

        Assert.IsType<HclVariableExpression>(cond.Condition);

        var t = Assert.IsType<HclLiteralExpression>(cond.TrueResult);
        Assert.Equal("yes", t.Value);

        var f = Assert.IsType<HclLiteralExpression>(cond.FalseResult);
        Assert.Equal("no", f.Value);
    }

    [Fact]
    public void ParseNestedConditional()
    {
        var file = Parse("x = a ? b ? \"bb\" : \"bn\" : \"no\"\n");
        var cond = Assert.IsType<HclConditionalExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.IsType<HclConditionalExpression>(cond.TrueResult);
        Assert.IsType<HclLiteralExpression>(cond.FalseResult);
    }

    [Fact]
    public void ParseConditionalWithOperators()
    {
        var file = Parse("x = a > 0 ? a * 2 : a * -1\n");
        var cond = Assert.IsType<HclConditionalExpression>(Assert.Single(file.Body.Attributes).Value);

        var gt = Assert.IsType<HclBinaryExpression>(cond.Condition);
        Assert.Equal(HclBinaryOperator.GreaterThan, gt.Operator);

        Assert.IsType<HclBinaryExpression>(cond.TrueResult);
        Assert.IsType<HclBinaryExpression>(cond.FalseResult);
    }

    // ── Parenthesized Expressions ───────────────────────────

    [Fact]
    public void ParseParenthesizedExpression()
    {
        var file = Parse("x = (a + b) * c\n");
        var attr = Assert.Single(file.Body.Attributes);

        var mul = Assert.IsType<HclBinaryExpression>(attr.Value);
        Assert.Equal(HclBinaryOperator.Multiply, mul.Operator);

        var add = Assert.IsType<HclBinaryExpression>(mul.Left);
        Assert.Equal(HclBinaryOperator.Add, add.Operator);
    }

    // ── Attribute Access (Dot) ──────────────────────────────

    [Fact]
    public void ParseAttributeAccess()
    {
        var file = Parse("x = var.name\n");
        var attr = Assert.Single(file.Body.Attributes);
        var access = Assert.IsType<HclAttributeAccessExpression>(attr.Value);
        Assert.Equal("name", access.Name);

        var source = Assert.IsType<HclVariableExpression>(access.Source);
        Assert.Equal("var", source.Name);
    }

    [Fact]
    public void ParseChainedAccess()
    {
        var file = Parse("x = module.network.vpc.id\n");
        var attr = Assert.Single(file.Body.Attributes);

        var id = Assert.IsType<HclAttributeAccessExpression>(attr.Value);
        Assert.Equal("id", id.Name);

        var vpc = Assert.IsType<HclAttributeAccessExpression>(id.Source);
        Assert.Equal("vpc", vpc.Name);

        var network = Assert.IsType<HclAttributeAccessExpression>(vpc.Source);
        Assert.Equal("network", network.Name);

        var module = Assert.IsType<HclVariableExpression>(network.Source);
        Assert.Equal("module", module.Name);
    }

    // ── Index Access ────────────────────────────────────────

    [Fact]
    public void ParseIndexAccess()
    {
        var file = Parse("x = list[0]\n");
        var attr = Assert.Single(file.Body.Attributes);
        var idx = Assert.IsType<HclIndexExpression>(attr.Value);
        Assert.False(idx.IsLegacy);

        Assert.IsType<HclVariableExpression>(idx.Collection);
        var index = Assert.IsType<HclLiteralExpression>(idx.Index);
        Assert.Equal("0", index.Value);
    }

    [Fact]
    public void ParseLegacyIndex()
    {
        var file = Parse("x = list.0\n");
        var attr = Assert.Single(file.Body.Attributes);
        var idx = Assert.IsType<HclIndexExpression>(attr.Value);
        Assert.True(idx.IsLegacy);
    }

    [Fact]
    public void ParseCombinedAccessAndIndex()
    {
        var file = Parse("x = module.network.subnets[0].id\n");
        var attr = Assert.Single(file.Body.Attributes);

        var id = Assert.IsType<HclAttributeAccessExpression>(attr.Value);
        Assert.Equal("id", id.Name);

        var idx = Assert.IsType<HclIndexExpression>(id.Source);
        Assert.False(idx.IsLegacy);

        var subnets = Assert.IsType<HclAttributeAccessExpression>(idx.Collection);
        Assert.Equal("subnets", subnets.Name);
    }

    // ── Splat Expressions ───────────────────────────────────

    [Fact]
    public void ParseAttributeSplat()
    {
        var file = Parse("x = list.*.name\n");
        var attr = Assert.Single(file.Body.Attributes);
        var splat = Assert.IsType<HclSplatExpression>(attr.Value);
        Assert.False(splat.IsFullSplat);
        Assert.IsType<HclVariableExpression>(splat.Source);
        Assert.Single(splat.Traversal);

        var trav = Assert.IsType<HclAttributeAccessExpression>(splat.Traversal[0]);
        Assert.Equal("name", trav.Name);
    }

    [Fact]
    public void ParseFullSplat()
    {
        var file = Parse("x = list[*].name\n");
        var attr = Assert.Single(file.Body.Attributes);
        var splat = Assert.IsType<HclSplatExpression>(attr.Value);
        Assert.True(splat.IsFullSplat);
        Assert.Single(splat.Traversal);
    }

    [Fact]
    public void ParseFullSplatIdentity()
    {
        var file = Parse("x = list[*]\n");
        var attr = Assert.Single(file.Body.Attributes);
        var splat = Assert.IsType<HclSplatExpression>(attr.Value);
        Assert.True(splat.IsFullSplat);
        Assert.Empty(splat.Traversal);
    }

    // ── Function Calls ──────────────────────────────────────

    [Fact]
    public void ParseFunctionCallNoArgs()
    {
        var file = Parse("x = timestamp()\n");
        var attr = Assert.Single(file.Body.Attributes);
        var func = Assert.IsType<HclFunctionCallExpression>(attr.Value);
        Assert.Equal("timestamp", func.Name);
        Assert.Empty(func.Arguments);
        Assert.False(func.ExpandFinalArgument);
    }

    [Fact]
    public void ParseFunctionCallWithArgs()
    {
        var file = Parse("x = substr(\"hello\", 0, 3)\n");
        var attr = Assert.Single(file.Body.Attributes);
        var func = Assert.IsType<HclFunctionCallExpression>(attr.Value);
        Assert.Equal("substr", func.Name);
        Assert.Equal(3, func.Arguments.Count);
    }

    [Fact]
    public void ParseFunctionCallWithExpansion()
    {
        var file = Parse("x = merge(map1, map2...)\n");
        var attr = Assert.Single(file.Body.Attributes);
        var func = Assert.IsType<HclFunctionCallExpression>(attr.Value);
        Assert.Equal("merge", func.Name);
        Assert.Equal(2, func.Arguments.Count);
        Assert.True(func.ExpandFinalArgument);
    }

    [Fact]
    public void ParseNestedFunctionCalls()
    {
        var file = Parse("x = upper(trim(\" hello \"))\n");
        var attr = Assert.Single(file.Body.Attributes);

        var outer = Assert.IsType<HclFunctionCallExpression>(attr.Value);
        Assert.Equal("upper", outer.Name);

        var inner = Assert.IsType<HclFunctionCallExpression>(Assert.Single(outer.Arguments));
        Assert.Equal("trim", inner.Name);
    }

    [Fact]
    public void ParseFunctionCallTrailingComma()
    {
        var file = Parse("x = list(\"a\", \"b\",)\n");
        var func = Assert.IsType<HclFunctionCallExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(2, func.Arguments.Count);
    }

    // ── Tuple Expressions ───────────────────────────────────

    [Fact]
    public void ParseEmptyTuple()
    {
        var file = Parse("x = []\n");
        var tuple = Assert.IsType<HclTupleExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Empty(tuple.Elements);
    }

    [Fact]
    public void ParseTupleWithElements()
    {
        var file = Parse("x = [1, 2, 3]\n");
        var tuple = Assert.IsType<HclTupleExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(3, tuple.Elements.Count);

        for (int i = 0; i < 3; i++)
        {
            var lit = Assert.IsType<HclLiteralExpression>(tuple.Elements[i]);
            Assert.Equal((i + 1).ToString(), lit.Value);
        }
    }

    [Fact]
    public void ParseTupleWithMixedTypes()
    {
        var file = Parse("x = [1, \"two\", true, null]\n");
        var tuple = Assert.IsType<HclTupleExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(4, tuple.Elements.Count);

        Assert.IsType<HclLiteralExpression>(tuple.Elements[0]);
        Assert.Equal(HclLiteralKind.Number, ((HclLiteralExpression)tuple.Elements[0]).Kind);
        Assert.Equal(HclLiteralKind.String, ((HclLiteralExpression)tuple.Elements[1]).Kind);
        Assert.Equal(HclLiteralKind.Bool, ((HclLiteralExpression)tuple.Elements[2]).Kind);
        Assert.Equal(HclLiteralKind.Null, ((HclLiteralExpression)tuple.Elements[3]).Kind);
    }

    [Fact]
    public void ParseTupleTrailingComma()
    {
        var file = Parse("x = [1, 2,]\n");
        var tuple = Assert.IsType<HclTupleExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(2, tuple.Elements.Count);
    }

    [Fact]
    public void ParseNestedTuples()
    {
        var file = Parse("x = [[1, 2], [3, 4]]\n");
        var outer = Assert.IsType<HclTupleExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(2, outer.Elements.Count);

        var inner1 = Assert.IsType<HclTupleExpression>(outer.Elements[0]);
        Assert.Equal(2, inner1.Elements.Count);
    }

    // ── Object Expressions ──────────────────────────────────

    [Fact]
    public void ParseEmptyObject()
    {
        var file = Parse("x = {}\n");
        var obj = Assert.IsType<HclObjectExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Empty(obj.Elements);
    }

    [Fact]
    public void ParseObjectWithEquals()
    {
        var file = Parse("x = { key = \"value\" }\n");
        var obj = Assert.IsType<HclObjectExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Single(obj.Elements);

        var elem = obj.Elements[0];
        Assert.False(elem.UsesColon);
        Assert.False(elem.ForceKey);

        var key = Assert.IsType<HclVariableExpression>(elem.Key);
        Assert.Equal("key", key.Name);

        var val = Assert.IsType<HclLiteralExpression>(elem.Value);
        Assert.Equal("value", val.Value);
    }

    [Fact]
    public void ParseObjectWithColon()
    {
        var file = Parse("x = { key: \"value\" }\n");
        var obj = Assert.IsType<HclObjectExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.True(obj.Elements[0].UsesColon);
    }

    [Fact]
    public void ParseObjectWithMultipleElements()
    {
        var file = Parse("x = { a = 1, b = 2 }\n");
        var obj = Assert.IsType<HclObjectExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(2, obj.Elements.Count);
    }

    [Fact]
    public void ParseObjectWithExpressionKey()
    {
        var file = Parse("x = { (var.key) = \"value\" }\n");
        var obj = Assert.IsType<HclObjectExpression>(Assert.Single(file.Body.Attributes).Value);

        var elem = obj.Elements[0];
        Assert.True(elem.ForceKey);
        Assert.IsType<HclAttributeAccessExpression>(elem.Key);
    }

    [Fact]
    public void ParseObjectWithStringKey()
    {
        var file = Parse("x = { \"key\" = \"value\" }\n");
        var obj = Assert.IsType<HclObjectExpression>(Assert.Single(file.Body.Attributes).Value);
        var key = Assert.IsType<HclLiteralExpression>(obj.Elements[0].Key);
        Assert.Equal("key", key.Value);
        Assert.Equal(HclLiteralKind.String, key.Kind);
    }

    // ── For Expressions ─────────────────────────────────────

    [Fact]
    public void ParseTupleFor()
    {
        var file = Parse("x = [for v in list : v]\n");
        var forExpr = Assert.IsType<HclForExpression>(Assert.Single(file.Body.Attributes).Value);

        Assert.Equal("v", forExpr.KeyVariable);
        Assert.Null(forExpr.ValueVariable);
        Assert.False(forExpr.IsObjectFor);
        Assert.False(forExpr.IsGrouped);
        Assert.Null(forExpr.Condition);

        Assert.IsType<HclVariableExpression>(forExpr.Collection);
        Assert.IsType<HclVariableExpression>(forExpr.ValueExpression);
    }

    [Fact]
    public void ParseTupleForWithIndex()
    {
        var file = Parse("x = [for i, v in list : v]\n");
        var forExpr = Assert.IsType<HclForExpression>(Assert.Single(file.Body.Attributes).Value);

        Assert.Equal("i", forExpr.KeyVariable);
        Assert.Equal("v", forExpr.ValueVariable);
    }

    [Fact]
    public void ParseTupleForWithCondition()
    {
        var file = Parse("x = [for v in list : v if v != \"\"]\n");
        var forExpr = Assert.IsType<HclForExpression>(Assert.Single(file.Body.Attributes).Value);

        Assert.NotNull(forExpr.Condition);
        var cond = Assert.IsType<HclBinaryExpression>(forExpr.Condition);
        Assert.Equal(HclBinaryOperator.NotEqual, cond.Operator);
    }

    [Fact]
    public void ParseObjectFor()
    {
        var file = Parse("x = {for k, v in map : k => v}\n");
        var forExpr = Assert.IsType<HclForExpression>(Assert.Single(file.Body.Attributes).Value);

        Assert.Equal("k", forExpr.KeyVariable);
        Assert.Equal("v", forExpr.ValueVariable);
        Assert.True(forExpr.IsObjectFor);
        Assert.NotNull(forExpr.KeyExpression);
        Assert.False(forExpr.IsGrouped);
    }

    [Fact]
    public void ParseObjectForWithGrouping()
    {
        var file = Parse("x = {for v in list : v.key => v...}\n");
        var forExpr = Assert.IsType<HclForExpression>(Assert.Single(file.Body.Attributes).Value);

        Assert.True(forExpr.IsObjectFor);
        Assert.True(forExpr.IsGrouped);
    }

    // ── Complex Expressions ─────────────────────────────────

    [Fact]
    public void ParseFunctionCallInExpression()
    {
        var file = Parse("x = length(var.list) + 1\n");
        var add = Assert.IsType<HclBinaryExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(HclBinaryOperator.Add, add.Operator);

        var func = Assert.IsType<HclFunctionCallExpression>(add.Left);
        Assert.Equal("length", func.Name);
    }

    [Fact]
    public void ParseConditionalWithFunctionCall()
    {
        var file = Parse("x = length(list) > 0 ? list[0] : \"default\"\n");
        var cond = Assert.IsType<HclConditionalExpression>(Assert.Single(file.Body.Attributes).Value);

        var gt = Assert.IsType<HclBinaryExpression>(cond.Condition);
        Assert.Equal(HclBinaryOperator.GreaterThan, gt.Operator);

        Assert.IsType<HclIndexExpression>(cond.TrueResult);
        Assert.IsType<HclLiteralExpression>(cond.FalseResult);
    }

    // ── Full Multi-Block File ───────────────────────────────

    [Fact]
    public void ParseFullTerraformFile()
    {
        var hcl = """
            variable "name" {
              default = "world"
            }

            resource "null_resource" "main" {
              triggers = {
                name = var.name
              }
            }

            output "greeting" {
              value = "Hello, ${var.name}!"
            }
            """;
        var file = Parse(hcl);

        Assert.Equal(3, file.Body.Blocks.Count);
        Assert.Equal("variable", file.Body.Blocks[0].Type);
        Assert.Equal("resource", file.Body.Blocks[1].Type);
        Assert.Equal("output", file.Body.Blocks[2].Type);

        // Variable block
        var varBlock = file.Body.Blocks[0];
        Assert.Single(varBlock.Labels);
        Assert.Equal("name", varBlock.Labels[0]);
        var defaultAttr = Assert.Single(varBlock.Body.Attributes);
        Assert.Equal("default", defaultAttr.Name);

        // Resource block — has triggers attribute with object value
        var resBlock = file.Body.Blocks[1];
        Assert.Equal(2, resBlock.Labels.Count);
        var triggersAttr = Assert.Single(resBlock.Body.Attributes);
        Assert.Equal("triggers", triggersAttr.Name);
        Assert.IsType<HclObjectExpression>(triggersAttr.Value);

        // Output block
        var outBlock = file.Body.Blocks[2];
        Assert.Single(outBlock.Labels);
        var valueAttr = Assert.Single(outBlock.Body.Attributes);
        Assert.Equal("value", valueAttr.Name);
    }

    // ── Deep Clone ──────────────────────────────────────────

    [Fact]
    public void DeepCloneProducesEqualTree()
    {
        var hcl = """
            resource "aws_instance" "main" {
              ami = "abc-123"
              count = 2
            }
            """;
        var original = Parse(hcl);
        var clone = (HclFile)original.DeepClone();

        Assert.NotSame(original, clone);
        Assert.NotSame(original.Body, clone.Body);

        Assert.Equal(original.Body.Blocks.Count, clone.Body.Blocks.Count);
        var origBlock = original.Body.Blocks[0];
        var cloneBlock = clone.Body.Blocks[0];

        Assert.NotSame(origBlock, cloneBlock);
        Assert.Equal(origBlock.Type, cloneBlock.Type);
        Assert.Equal(origBlock.Labels.Count, cloneBlock.Labels.Count);
        Assert.Equal(origBlock.Body.Attributes.Count, cloneBlock.Body.Attributes.Count);
    }

    [Fact]
    public void DeepCloneIsIndependent()
    {
        var file = Parse("x = 42\n");
        var clone = (HclFile)file.DeepClone();

        // Mutating clone does not affect original
        clone.Body.Attributes[0] = new HclAttribute
        {
            Name = "y",
            Value = new HclLiteralExpression
            {
                Value = "99",
                Kind = HclLiteralKind.Number,
            },
        };

        Assert.Equal("x", file.Body.Attributes[0].Name);
        Assert.Equal("y", clone.Body.Attributes[0].Name);
    }

    // ── Visitor Traversal ───────────────────────────────────

    [Fact]
    public void VisitorTraversesAllNodes()
    {
        var file = Parse("x = 42\n");
        var visitor = new TestVisitor();
        file.Accept(visitor);

        Assert.Contains("File", visitor.Visited);
        Assert.Contains("Body", visitor.Visited);
        Assert.Contains("Attribute", visitor.Visited);
        Assert.Contains("Literal", visitor.Visited);
    }

    [Fact]
    public void VisitorTraversesBlockTree()
    {
        var file = Parse("""
            resource "type" "name" {
              attr = true
            }
            """);
        var visitor = new TestVisitor();
        file.Accept(visitor);

        Assert.Contains("File", visitor.Visited);
        Assert.Contains("Block", visitor.Visited);
        Assert.Contains("Attribute", visitor.Visited);
    }

    // ── Comment Preservation ────────────────────────────────

    [Fact]
    public void ParsePreservesLeadingCommentOnAttribute()
    {
        var file = Parse("// a comment\nx = 1\n");

        var attr = Assert.Single(file.Body.Attributes);
        Assert.Single(attr.LeadingComments);
        Assert.Contains("a comment", attr.LeadingComments[0].Text);
    }

    [Fact]
    public void ParsePreservesLeadingCommentOnBlock()
    {
        var file = Parse("# block comment\nresource \"a\" \"b\" {}\n");

        var block = Assert.Single(file.Body.Blocks);
        Assert.Single(block.LeadingComments);
        Assert.Equal(HclCommentStyle.Hash, block.LeadingComments[0].Style);
    }

    [Fact]
    public void DanglingCommentsAtEndOfFile()
    {
        var file = Parse("x = 1\n// trailing\n");

        Assert.Single(file.Body.Attributes);
        Assert.Single(file.DanglingComments);
    }

    // ── Edge Cases ──────────────────────────────────────────

    [Fact]
    public void ParseEmptyFile()
    {
        var file = Parse("");
        Assert.Empty(file.Body.Attributes);
        Assert.Empty(file.Body.Blocks);
    }

    [Fact]
    public void ParseAttributeAtEofWithoutNewline()
    {
        var file = Parse("x = 1");
        var attr = Assert.Single(file.Body.Attributes);
        Assert.Equal("x", attr.Name);
    }

    [Fact]
    public void ParseBlockAtEofWithoutNewline()
    {
        var file = Parse("lifecycle {}");
        var block = Assert.Single(file.Body.Blocks);
        Assert.Equal("lifecycle", block.Type);
    }

    [Fact]
    public void ParseObjectNewlineSeparated()
    {
        var hcl = "x = {\n  a = 1\n  b = 2\n}\n";
        var file = Parse(hcl);
        var obj = Assert.IsType<HclObjectExpression>(Assert.Single(file.Body.Attributes).Value);
        Assert.Equal(2, obj.Elements.Count);
    }

    /// <summary>
    /// Simple visitor that records which node types were visited.
    /// </summary>
    private sealed class TestVisitor : IHclVisitor
    {
        public List<string> Visited { get; } = [];

        public void VisitFile(HclFile node)
        {
            Visited.Add("File");
            node.Body.Accept(this);
        }

        public void VisitBody(HclBody node)
        {
            Visited.Add("Body");
            foreach (var attr in node.Attributes)
            {
                attr.Accept(this);
            }

            foreach (var block in node.Blocks)
            {
                block.Accept(this);
            }
        }

        public void VisitBlock(HclBlock node)
        {
            Visited.Add("Block");
            node.Body.Accept(this);
        }

        public void VisitAttribute(HclAttribute node)
        {
            Visited.Add("Attribute");
            node.Value.Accept(this);
        }

        public void VisitLiteral(HclLiteralExpression node) => Visited.Add("Literal");
        public void VisitTemplate(HclTemplateExpression node) => Visited.Add("Template");
        public void VisitTemplateWrap(HclTemplateWrapExpression node) => Visited.Add("TemplateWrap");
        public void VisitVariable(HclVariableExpression node) => Visited.Add("Variable");
        public void VisitIndex(HclIndexExpression node) => Visited.Add("Index");
        public void VisitAttributeAccess(HclAttributeAccessExpression node) => Visited.Add("AttributeAccess");
        public void VisitSplat(HclSplatExpression node) => Visited.Add("Splat");
        public void VisitUnary(HclUnaryExpression node) => Visited.Add("Unary");
        public void VisitBinary(HclBinaryExpression node) => Visited.Add("Binary");
        public void VisitConditional(HclConditionalExpression node) => Visited.Add("Conditional");
        public void VisitFunctionCall(HclFunctionCallExpression node) => Visited.Add("FunctionCall");
        public void VisitFor(HclForExpression node) => Visited.Add("For");
        public void VisitTuple(HclTupleExpression node) => Visited.Add("Tuple");
        public void VisitObject(HclObjectExpression node) => Visited.Add("Object");
    }
}
