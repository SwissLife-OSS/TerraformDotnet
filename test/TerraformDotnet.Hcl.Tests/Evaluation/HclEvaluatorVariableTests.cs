using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorVariableTests
{
    private readonly HclEvaluator _evaluator = new();

    [Fact]
    public void DefinedVariableReturnsValue()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("name", HclValue.FromString("world"));
        var expr = new HclVariableExpression { Name = "name" };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(HclValueType.String, result.Type);
        Assert.Equal("world", result.StringValue);
    }

    [Fact]
    public void UndefinedVariableThrows()
    {
        var ctx = new HclEvaluationContext();
        var expr = new HclVariableExpression { Name = "missing" };

        var ex = Assert.Throws<HclUnresolvableException>(() => _evaluator.Evaluate(expr, ctx));
        Assert.Equal("missing", ex.VariableName);
    }

    [Fact]
    public void NestedVariableAccessViaAttributeAccess()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("var", HclValue.FromObject(new Dictionary<string, HclValue>
        {
            ["name"] = HclValue.FromString("myapp"),
        }));

        // var.name → attribute access: Source=VariableExpr("var"), Name="name"
        var expr = new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "var" },
            Name = "name",
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal("myapp", result.StringValue);
    }

    [Fact]
    public void ChainedAttributeAccess()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("module", HclValue.FromObject(new Dictionary<string, HclValue>
        {
            ["network"] = HclValue.FromObject(new Dictionary<string, HclValue>
            {
                ["vpc"] = HclValue.FromObject(new Dictionary<string, HclValue>
                {
                    ["id"] = HclValue.FromString("vpc-123"),
                }),
            }),
        }));

        // module.network.vpc.id
        var expr = new HclAttributeAccessExpression
        {
            Source = new HclAttributeAccessExpression
            {
                Source = new HclAttributeAccessExpression
                {
                    Source = new HclVariableExpression { Name = "module" },
                    Name = "network",
                },
                Name = "vpc",
            },
            Name = "id",
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal("vpc-123", result.StringValue);
    }

    [Fact]
    public void IndexAccessOnTuple()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("list", HclValue.FromTuple([
            HclValue.FromString("a"),
            HclValue.FromString("b"),
            HclValue.FromString("c"),
        ]));

        // list[1]
        var expr = new HclIndexExpression
        {
            Collection = new HclVariableExpression { Name = "list" },
            Index = new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal("b", result.StringValue);
    }

    [Fact]
    public void IndexAccessOnObject()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("map", HclValue.FromObject(new Dictionary<string, HclValue>
        {
            ["key"] = HclValue.FromNumber(42),
        }));

        // map["key"]
        var expr = new HclIndexExpression
        {
            Collection = new HclVariableExpression { Name = "map" },
            Index = new HclLiteralExpression { Value = "key", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(42.0, result.NumberValue);
    }

    [Fact]
    public void VariableShadowingInChildScope()
    {
        var parent = new HclEvaluationContext();
        parent.SetVariable("x", HclValue.FromNumber(1));

        var child = parent.CreateChildScope();
        child.SetVariable("x", HclValue.FromNumber(2));

        var expr = new HclVariableExpression { Name = "x" };

        var parentResult = _evaluator.Evaluate(expr, parent);
        var childResult = _evaluator.Evaluate(expr, child);

        Assert.Equal(1.0, parentResult.NumberValue);
        Assert.Equal(2.0, childResult.NumberValue);
    }

    [Fact]
    public void ChildScopeInheritsParent()
    {
        var parent = new HclEvaluationContext();
        parent.SetVariable("inherited", HclValue.FromString("from-parent"));

        var child = parent.CreateChildScope();

        var expr = new HclVariableExpression { Name = "inherited" };

        var result = _evaluator.Evaluate(expr, child);

        Assert.Equal("from-parent", result.StringValue);
    }

    [Fact]
    public void AttributeAccessOnNonObjectThrows()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("x", HclValue.FromNumber(42));

        var expr = new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "x" },
            Name = "attr",
        };

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(expr, ctx));
    }

    [Fact]
    public void MissingObjectAttributeThrows()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("obj", HclValue.FromObject(new Dictionary<string, HclValue>
        {
            ["a"] = HclValue.FromNumber(1),
        }));

        var expr = new HclAttributeAccessExpression
        {
            Source = new HclVariableExpression { Name = "obj" },
            Name = "missing",
        };

        var ex = Assert.Throws<HclUnresolvableException>(() => _evaluator.Evaluate(expr, ctx));
        Assert.Equal("missing", ex.VariableName);
    }
}
