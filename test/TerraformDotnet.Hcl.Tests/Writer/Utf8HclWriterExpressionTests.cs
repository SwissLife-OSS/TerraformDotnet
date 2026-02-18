using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.Writer;

public sealed class Utf8HclWriterExpressionTests
{
    private static string Emit(HclFile file)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        var emitter = new HclFileEmitter(writer);
        emitter.Emit(file);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static HclFile ParseAndEmit(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return HclFileParser.Parse(bytes);
    }

    [Fact]
    public void EmitConditional()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "x",
            Value = new HclConditionalExpression
            {
                Condition = new HclVariableExpression { Name = "enabled" },
                TrueResult = new HclLiteralExpression { Value = "yes", Kind = HclLiteralKind.String },
                FalseResult = new HclLiteralExpression { Value = "no", Kind = HclLiteralKind.String },
            },
        });

        string result = Emit(file);
        Assert.Equal("x = enabled ? \"yes\" : \"no\"\n", result);
    }

    [Fact]
    public void EmitForTupleExpression()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "ids",
            Value = new HclForExpression
            {
                KeyVariable = "v",
                Collection = new HclVariableExpression { Name = "list" },
                ValueExpression = new HclAttributeAccessExpression
                {
                    Source = new HclVariableExpression { Name = "v" },
                    Name = "id",
                },
                IsObjectFor = false,
            },
        });

        string result = Emit(file);
        Assert.Equal("ids = [for v in list : v.id]\n", result);
    }

    [Fact]
    public void EmitForObjectExpression()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "map",
            Value = new HclForExpression
            {
                KeyVariable = "k",
                ValueVariable = "v",
                Collection = new HclVariableExpression { Name = "items" },
                KeyExpression = new HclVariableExpression { Name = "k" },
                ValueExpression = new HclVariableExpression { Name = "v" },
                IsObjectFor = true,
            },
        });

        string result = Emit(file);
        Assert.Equal("map = {for k, v in items : k => v}\n", result);
    }

    [Fact]
    public void EmitSplatExpression()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "ids",
            Value = new HclSplatExpression
            {
                Source = new HclVariableExpression { Name = "instances" },
                IsFullSplat = true,
                Traversal = { new HclAttributeAccessExpression
                {
                    Source = new HclVariableExpression { Name = "_" }, // placeholder
                    Name = "id",
                }},
            },
        });

        string result = Emit(file);
        Assert.Equal("ids = instances[*].id\n", result);
    }

    [Fact]
    public void EmitFunctionCall()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "result",
            Value = new HclFunctionCallExpression
            {
                Name = "max",
                Arguments =
                {
                    new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
                    new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number },
                    new HclLiteralExpression { Value = "3", Kind = HclLiteralKind.Number },
                },
            },
        });

        string result = Emit(file);
        Assert.Equal("result = max(1, 2, 3)\n", result);
    }

    [Fact]
    public void EmitFunctionCallWithExpansion()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "result",
            Value = new HclFunctionCallExpression
            {
                Name = "concat",
                Arguments =
                {
                    new HclVariableExpression { Name = "list" },
                },
                ExpandFinalArgument = true,
            },
        });

        string result = Emit(file);
        Assert.Equal("result = concat(list...)\n", result);
    }

    [Fact]
    public void EmitBinaryOperation()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "sum",
            Value = new HclBinaryExpression
            {
                Left = new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
                Operator = HclBinaryOperator.Add,
                Right = new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number },
            },
        });

        string result = Emit(file);
        Assert.Equal("sum = 1 + 2\n", result);
    }

    [Fact]
    public void EmitUnaryOperation()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "neg",
            Value = new HclUnaryExpression
            {
                Operator = HclUnaryOperator.Negate,
                Operand = new HclLiteralExpression { Value = "5", Kind = HclLiteralKind.Number },
            },
        });

        string result = Emit(file);
        Assert.Equal("neg = -5\n", result);
    }

    [Fact]
    public void EmitNotExpression()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "neg",
            Value = new HclUnaryExpression
            {
                Operator = HclUnaryOperator.Not,
                Operand = new HclVariableExpression { Name = "enabled" },
            },
        });

        string result = Emit(file);
        Assert.Equal("neg = !enabled\n", result);
    }

    [Fact]
    public void EmitComplexNestedExpression()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "value",
            Value = new HclBinaryExpression
            {
                Left = new HclBinaryExpression
                {
                    Left = new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
                    Operator = HclBinaryOperator.Add,
                    Right = new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number },
                },
                Operator = HclBinaryOperator.Multiply,
                Right = new HclLiteralExpression { Value = "3", Kind = HclLiteralKind.Number },
            },
        });

        string result = Emit(file);
        Assert.Equal("value = 1 + 2 * 3\n", result);
    }

    [Fact]
    public void EmitAllBinaryOperators()
    {
        var ops = new (HclBinaryOperator Op, string Str)[]
        {
            (HclBinaryOperator.Add, "+"),
            (HclBinaryOperator.Subtract, "-"),
            (HclBinaryOperator.Multiply, "*"),
            (HclBinaryOperator.Divide, "/"),
            (HclBinaryOperator.Modulo, "%"),
            (HclBinaryOperator.Equal, "=="),
            (HclBinaryOperator.NotEqual, "!="),
            (HclBinaryOperator.LessThan, "<"),
            (HclBinaryOperator.GreaterThan, ">"),
            (HclBinaryOperator.LessEqual, "<="),
            (HclBinaryOperator.GreaterEqual, ">="),
            (HclBinaryOperator.And, "&&"),
            (HclBinaryOperator.Or, "||"),
        };

        foreach (var (op, str) in ops)
        {
            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute
            {
                Name = "x",
                Value = new HclBinaryExpression
                {
                    Left = new HclLiteralExpression { Value = "a", Kind = HclLiteralKind.Number },
                    Operator = op,
                    Right = new HclLiteralExpression { Value = "b", Kind = HclLiteralKind.Number },
                },
            });

            string result = Emit(file);
            Assert.Contains($" {str} ", result);
        }
    }

    [Fact]
    public void EmitEmptyTuple()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "list",
            Value = new HclTupleExpression(),
        });

        string result = Emit(file);
        Assert.Equal("list = []\n", result);
    }

    [Fact]
    public void EmitEmptyObject()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "obj",
            Value = new HclObjectExpression(),
        });

        string result = Emit(file);
        Assert.Equal("obj = {}\n", result);
    }

    [Fact]
    public void EmitObjectWithMultipleKeys()
    {
        var file = new HclFile();
        var objExpr = new HclObjectExpression
        {
            Elements =
            {
                new HclObjectElement
                {
                    Key = new HclLiteralExpression { Value = "Name", Kind = HclLiteralKind.String },
                    Value = new HclLiteralExpression { Value = "main", Kind = HclLiteralKind.String },
                },
                new HclObjectElement
                {
                    Key = new HclLiteralExpression { Value = "Env", Kind = HclLiteralKind.String },
                    Value = new HclLiteralExpression { Value = "prod", Kind = HclLiteralKind.String },
                },
            },
        };

        file.Body.Attributes.Add(new HclAttribute { Name = "tags", Value = objExpr });

        string result = Emit(file);
        Assert.Contains("\"Name\" = \"main\"", result);
        Assert.Contains("\"Env\"  = \"prod\"", result);
    }

    [Fact]
    public void EmitAttributeAccess()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "id",
            Value = new HclAttributeAccessExpression
            {
                Source = new HclAttributeAccessExpression
                {
                    Source = new HclVariableExpression { Name = "aws_instance" },
                    Name = "main",
                },
                Name = "id",
            },
        });

        string result = Emit(file);
        Assert.Equal("id = aws_instance.main.id\n", result);
    }

    [Fact]
    public void EmitIndexAccess()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "first",
            Value = new HclIndexExpression
            {
                Collection = new HclVariableExpression { Name = "list" },
                Index = new HclLiteralExpression { Value = "0", Kind = HclLiteralKind.Number },
            },
        });

        string result = Emit(file);
        Assert.Equal("first = list[0]\n", result);
    }

    [Fact]
    public void EmitTemplateWrap()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "name",
            Value = new HclTemplateWrapExpression
            {
                Wrapped = new HclVariableExpression { Name = "var.name" },
            },
        });

        string result = Emit(file);
        Assert.Equal("name = \"${var.name}\"\n", result);
    }

    [Fact]
    public void EmitForWithCondition()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "filtered",
            Value = new HclForExpression
            {
                KeyVariable = "v",
                Collection = new HclVariableExpression { Name = "list" },
                ValueExpression = new HclVariableExpression { Name = "v" },
                Condition = new HclBinaryExpression
                {
                    Left = new HclVariableExpression { Name = "v" },
                    Operator = HclBinaryOperator.GreaterThan,
                    Right = new HclLiteralExpression { Value = "0", Kind = HclLiteralKind.Number },
                },
                IsObjectFor = false,
            },
        });

        string result = Emit(file);
        Assert.Equal("filtered = [for v in list : v if v > 0]\n", result);
    }

    [Fact]
    public void EmitForObjectWithGrouping()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute
        {
            Name = "grouped",
            Value = new HclForExpression
            {
                KeyVariable = "k",
                ValueVariable = "v",
                Collection = new HclVariableExpression { Name = "items" },
                KeyExpression = new HclVariableExpression { Name = "k" },
                ValueExpression = new HclVariableExpression { Name = "v" },
                IsObjectFor = true,
                IsGrouped = true,
            },
        });

        string result = Emit(file);
        Assert.Equal("grouped = {for k, v in items : k => v...}\n", result);
    }
}
