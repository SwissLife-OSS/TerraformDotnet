using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorCollectionTests
{
    private readonly HclEvaluator _evaluator = new();
    private readonly HclEvaluationContext _context = new();

    [Fact]
    public void TupleConstructor()
    {
        var expr = new HclTupleExpression();
        expr.Elements.Add(new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number });
        expr.Elements.Add(new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number });
        expr.Elements.Add(new HclLiteralExpression { Value = "3", Kind = HclLiteralKind.Number });

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Tuple, result.Type);
        Assert.Equal(3, result.TupleValue.Count);
        Assert.Equal(1.0, result.TupleValue[0].NumberValue);
        Assert.Equal(2.0, result.TupleValue[1].NumberValue);
        Assert.Equal(3.0, result.TupleValue[2].NumberValue);
    }

    [Fact]
    public void EmptyTuple()
    {
        var expr = new HclTupleExpression();

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Tuple, result.Type);
        Assert.Empty(result.TupleValue);
    }

    [Fact]
    public void ObjectConstructor()
    {
        var expr = new HclObjectExpression();
        expr.Elements.Add(new HclObjectElement
        {
            Key = new HclLiteralExpression { Value = "a", Kind = HclLiteralKind.String },
            Value = new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
        });
        expr.Elements.Add(new HclObjectElement
        {
            Key = new HclLiteralExpression { Value = "b", Kind = HclLiteralKind.String },
            Value = new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number },
        });

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Object, result.Type);
        Assert.Equal(2, result.ObjectValue.Count);
        Assert.Equal(1.0, result.ObjectValue["a"].NumberValue);
        Assert.Equal(2.0, result.ObjectValue["b"].NumberValue);
    }

    [Fact]
    public void EmptyObject()
    {
        var expr = new HclObjectExpression();

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Object, result.Type);
        Assert.Empty(result.ObjectValue);
    }

    [Fact]
    public void ForTupleExpression()
    {
        // [for v in [1, 2, 3] : v * 2]
        var ctx = new HclEvaluationContext();
        var collection = new HclTupleExpression();
        collection.Elements.Add(new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number });
        collection.Elements.Add(new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number });
        collection.Elements.Add(new HclLiteralExpression { Value = "3", Kind = HclLiteralKind.Number });

        var expr = new HclForExpression
        {
            KeyVariable = "v",
            Collection = collection,
            ValueExpression = new HclBinaryExpression
            {
                Left = new HclVariableExpression { Name = "v" },
                Operator = HclBinaryOperator.Multiply,
                Right = new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number },
            },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(HclValueType.Tuple, result.Type);
        Assert.Equal(3, result.TupleValue.Count);
        Assert.Equal(2.0, result.TupleValue[0].NumberValue);
        Assert.Equal(4.0, result.TupleValue[1].NumberValue);
        Assert.Equal(6.0, result.TupleValue[2].NumberValue);
    }

    [Fact]
    public void ForObjectExpression()
    {
        // {for k, v in {a = 1} : k => v + 1}
        var ctx = new HclEvaluationContext();
        var collection = new HclObjectExpression();
        collection.Elements.Add(new HclObjectElement
        {
            Key = new HclLiteralExpression { Value = "a", Kind = HclLiteralKind.String },
            Value = new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
        });

        var expr = new HclForExpression
        {
            KeyVariable = "k",
            ValueVariable = "v",
            Collection = collection,
            KeyExpression = new HclVariableExpression { Name = "k" },
            ValueExpression = new HclBinaryExpression
            {
                Left = new HclVariableExpression { Name = "v" },
                Operator = HclBinaryOperator.Add,
                Right = new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
            },
            IsObjectFor = true,
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(HclValueType.Object, result.Type);
        Assert.Equal(2.0, result.ObjectValue["a"].NumberValue);
    }

    [Fact]
    public void ForWithCondition()
    {
        // [for v in [1, 2, 3, 4] : v if v > 2]
        var ctx = new HclEvaluationContext();
        var collection = new HclTupleExpression();
        collection.Elements.Add(new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number });
        collection.Elements.Add(new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number });
        collection.Elements.Add(new HclLiteralExpression { Value = "3", Kind = HclLiteralKind.Number });
        collection.Elements.Add(new HclLiteralExpression { Value = "4", Kind = HclLiteralKind.Number });

        var expr = new HclForExpression
        {
            KeyVariable = "v",
            Collection = collection,
            ValueExpression = new HclVariableExpression { Name = "v" },
            Condition = new HclBinaryExpression
            {
                Left = new HclVariableExpression { Name = "v" },
                Operator = HclBinaryOperator.GreaterThan,
                Right = new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number },
            },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(HclValueType.Tuple, result.Type);
        Assert.Equal(2, result.TupleValue.Count);
        Assert.Equal(3.0, result.TupleValue[0].NumberValue);
        Assert.Equal(4.0, result.TupleValue[1].NumberValue);
    }

    [Fact]
    public void ForObjectWithGrouping()
    {
        // {for v in ["a", "b", "a"] : v => v...} → {a = ["a", "a"], b = ["b"]}
        var ctx = new HclEvaluationContext();
        var collection = new HclTupleExpression();
        collection.Elements.Add(new HclLiteralExpression { Value = "a", Kind = HclLiteralKind.String });
        collection.Elements.Add(new HclLiteralExpression { Value = "b", Kind = HclLiteralKind.String });
        collection.Elements.Add(new HclLiteralExpression { Value = "a", Kind = HclLiteralKind.String });

        var expr = new HclForExpression
        {
            KeyVariable = "v",
            Collection = collection,
            KeyExpression = new HclVariableExpression { Name = "v" },
            ValueExpression = new HclVariableExpression { Name = "v" },
            IsObjectFor = true,
            IsGrouped = true,
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(HclValueType.Object, result.Type);
        Assert.Equal(2, result.ObjectValue.Count);
        Assert.Equal(HclValueType.Tuple, result.ObjectValue["a"].Type);
        Assert.Equal(2, result.ObjectValue["a"].TupleValue.Count);
        Assert.Equal("a", result.ObjectValue["a"].TupleValue[0].StringValue);
        Assert.Equal(HclValueType.Tuple, result.ObjectValue["b"].Type);
        Assert.Single(result.ObjectValue["b"].TupleValue);
    }

    [Fact]
    public void ForWithIndexAndValue()
    {
        // [for i, v in ["x", "y"] : i] → [0, 1]
        var ctx = new HclEvaluationContext();
        var collection = new HclTupleExpression();
        collection.Elements.Add(new HclLiteralExpression { Value = "x", Kind = HclLiteralKind.String });
        collection.Elements.Add(new HclLiteralExpression { Value = "y", Kind = HclLiteralKind.String });

        var expr = new HclForExpression
        {
            KeyVariable = "i",
            ValueVariable = "v",
            Collection = collection,
            ValueExpression = new HclVariableExpression { Name = "i" },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(HclValueType.Tuple, result.Type);
        Assert.Equal(2, result.TupleValue.Count);
        Assert.Equal(0.0, result.TupleValue[0].NumberValue);
        Assert.Equal(1.0, result.TupleValue[1].NumberValue);
    }

    [Fact]
    public void UnknownInTuplePropagates()
    {
        var expr = new HclTupleExpression();
        expr.Elements.Add(new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number });
        expr.Elements.Add(new HclFunctionCallExpression { Name = "unknown_fn" });

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void NestedCollection()
    {
        var inner = new HclTupleExpression();
        inner.Elements.Add(new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number });

        var outer = new HclTupleExpression();
        outer.Elements.Add(inner);
        outer.Elements.Add(new HclLiteralExpression { Value = "2", Kind = HclLiteralKind.Number });

        var result = _evaluator.Evaluate(outer, _context);

        Assert.Equal(HclValueType.Tuple, result.Type);
        Assert.Equal(2, result.TupleValue.Count);
        Assert.Equal(HclValueType.Tuple, result.TupleValue[0].Type);
        Assert.Equal(1.0, result.TupleValue[0].TupleValue[0].NumberValue);
    }
}
