using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorUnknownPropagationTests
{
    private static HclValue Evaluate(string expression, HclEvaluatorOptions? options = null, params (string Name, HclValue Value)[] variables)
    {
        var context = new HclEvaluationContext();
        foreach (var (name, value) in variables)
        {
            context.SetVariable(name, value);
        }

        return new HclEvaluator(options ?? new HclEvaluatorOptions()).Evaluate(HclExpression.Parse(expression), context);
    }

    private static readonly HclValue Unknown = HclValue.Unknown("test");

    private static HclValue Tuple(params HclValue[] values) => HclValue.FromTuple(values);

    [Fact]
    public void ForTupleWithUnknownConditionIsUnknown()
    {
        var result = Evaluate("[for x in list : x if u]", variables: [("list", Tuple(HclValue.FromNumber(1))), ("u", Unknown)]);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void ForObjectWithUnknownConditionIsUnknown()
    {
        var result = Evaluate("{for x in list : x => x if u}", variables: [("list", Tuple(HclValue.FromString("a"))), ("u", Unknown)]);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void ForTupleWithUnknownElementResultIsUnknown()
    {
        var result = Evaluate("[for x in list : u]", variables: [("list", Tuple(HclValue.FromNumber(1))), ("u", Unknown)]);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void ForConditionMustBeBoolean()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Evaluate("[for x in list : x if 1]", variables: ("list", Tuple(HclValue.FromNumber(1)))));
    }

    [Fact]
    public void ForConditionAcceptsBooleanString()
    {
        var result = Evaluate("[for x in list : x if \"true\"]", variables: ("list", Tuple(HclValue.FromNumber(1))));

        Assert.Equal(Tuple(HclValue.FromNumber(1)), result);
    }

    [Fact]
    public void ForOverNullFails()
    {
        Assert.Throws<InvalidOperationException>(() => Evaluate("[for x in nothing : x]", variables: ("nothing", HclValue.Null)));
    }

    [Fact]
    public void UndefinedRootThrowsByDefault()
    {
        Assert.Throws<HclUnresolvableException>(() => Evaluate("data.thing.id"));
    }

    [Fact]
    public void UndefinedRootIsUnknownWhenConfigured()
    {
        var result = Evaluate("data.thing.id", new HclEvaluatorOptions { TreatUndefinedVariablesAsUnknown = true });

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Equal("data", result.UnknownSource);
    }

    [Fact]
    public void UnknownRootDecidesNothingInLogic()
    {
        var options = new HclEvaluatorOptions { TreatUndefinedVariablesAsUnknown = true };

        Assert.Equal(HclValue.False, Evaluate("false && data.x == 1", options));
        Assert.Equal(HclValueType.Unknown, Evaluate("true && data.x == 1", options).Type);
    }

    [Fact]
    public void SplatOfNullIsEmptyTuple()
    {
        var result = Evaluate("x[*].name", variables: ("x", HclValue.Null));

        Assert.Equal(Tuple(), result);
    }

    [Fact]
    public void SplatOfSingleObjectIsOneElementTuple()
    {
        var obj = HclValue.FromObject(new Dictionary<string, HclValue> { ["name"] = HclValue.FromString("a") });

        var result = Evaluate("x[*].name", variables: ("x", obj));

        Assert.Equal(Tuple(HclValue.FromString("a")), result);
    }

    [Fact]
    public void SplatSupportsTupleValuedIndexTraversal()
    {
        var inner = Tuple(HclValue.FromString("a"), HclValue.FromString("b"));
        var obj = HclValue.FromObject(new Dictionary<string, HclValue> { ["tags"] = inner });

        var result = Evaluate("x[*].tags[1]", variables: ("x", Tuple(obj)));

        Assert.Equal(Tuple(HclValue.FromString("b")), result);
    }

    [Fact]
    public void TupleIndexAcceptsNumericStrings()
    {
        var result = Evaluate("x[\"1\"]", variables: ("x", Tuple(HclValue.FromNumber(1), HclValue.FromNumber(2))));

        Assert.Equal(HclValue.FromNumber(2), result);
    }

    [Theory]
    [InlineData("x[2]")]
    [InlineData("x[-1]")]
    [InlineData("x[0.5]")]
    public void TupleIndexOutOfRangeFailsWithEvaluationError(string expression)
    {
        Assert.Throws<HclUnresolvableException>(() =>
            Evaluate(expression, variables: ("x", Tuple(HclValue.FromNumber(1), HclValue.FromNumber(2)))));
    }

    [Fact]
    public void LegacyTupleIndexSyntaxWorks()
    {
        var result = Evaluate("x.1", variables: ("x", Tuple(HclValue.FromNumber(1), HclValue.FromNumber(2))));

        Assert.Equal(HclValue.FromNumber(2), result);
    }

    [Fact]
    public void RunawayLoopsAreAborted()
    {
        var options = new HclEvaluatorOptions { MaxIterations = 5 };
        var list = Tuple(Enumerable.Range(0, 10).Select(i => HclValue.FromNumber(i)).ToArray());

        Assert.Throws<HclEvaluationLimitException>(() => Evaluate("[for x in list : x]", options, ("list", list)));
    }

    [Fact]
    public void LimitViolationsAreNotSwallowedByCan()
    {
        var options = new HclEvaluatorOptions { MaxIterations = 5 };
        var list = Tuple(Enumerable.Range(0, 10).Select(i => HclValue.FromNumber(i)).ToArray());

        Assert.Throws<HclEvaluationLimitException>(() => Evaluate("can([for x in list : x])", options, ("list", list)));
    }

    [Fact]
    public void DepthLimitIsConfigurable()
    {
        var options = new HclEvaluatorOptions { MaxDepth = 3 };

        Assert.Throws<HclEvaluationLimitException>(() => Evaluate("[[[[1]]]]", options));
    }
}
