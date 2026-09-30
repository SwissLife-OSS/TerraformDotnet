using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorFunctionResolverTests
{
    private sealed class TestResolver : IHclFunctionResolver
    {
        public List<(string Name, IReadOnlyList<HclValue> Arguments)> Calls { get; } = [];

        public HclValue? Invoke(string name, IReadOnlyList<HclValue> arguments)
        {
            Calls.Add((name, arguments));

            return name switch
            {
                "upper" => HclValue.FromString(arguments[0].StringValue.ToUpperInvariant()),
                "sum" => HclValue.FromNumber(arguments.Sum(a => a.NumberValue)),
                "fail" => throw new HclFunctionException(name, "always fails"),
                _ => null,
            };
        }
    }

    private readonly TestResolver _resolver = new();

    private HclValue Evaluate(string expression, HclEvaluatorOptions? options = null, params (string Name, HclValue Value)[] variables)
    {
        var context = new HclEvaluationContext();
        foreach (var (name, value) in variables)
        {
            context.SetVariable(name, value);
        }

        var evaluator = new HclEvaluator(options ?? new HclEvaluatorOptions { FunctionResolver = _resolver });

        return evaluator.Evaluate(HclExpression.Parse(expression), context);
    }

    [Fact]
    public void ResolverResultIsReturned()
    {
        var result = Evaluate("upper(\"abc\")");

        Assert.Equal(HclValue.FromString("ABC"), result);
    }

    [Fact]
    public void ResolverReturningNullYieldsUnknownWithArguments()
    {
        var result = Evaluate("something_else(1, 2)");

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Equal("something_else", result.UnknownSource);
        Assert.Equal(2, result.UnknownArgs.Count);
    }

    [Fact]
    public void ResolverIsNotCalledWithUnknownArguments()
    {
        var result = Evaluate("upper(u)", variables: ("u", HclValue.Unknown("test")));

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Empty(_resolver.Calls);
    }

    [Fact]
    public void FinalArgumentCanBeExpanded()
    {
        var result = Evaluate("sum(1, list...)", variables: ("list", HclValue.FromTuple([HclValue.FromNumber(2), HclValue.FromNumber(3)])));

        Assert.Equal(HclValue.FromNumber(6), result);
        Assert.Equal(3, _resolver.Calls[0].Arguments.Count);
    }

    [Fact]
    public void ExpandingANonTupleFails()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Evaluate("sum(1, x...)", variables: ("x", HclValue.FromNumber(2))));
    }

    [Fact]
    public void FunctionExceptionPropagatesWithFunctionName()
    {
        var ex = Assert.Throws<HclFunctionException>(() => Evaluate("fail()"));

        Assert.Equal("fail", ex.FunctionName);
    }

    [Fact]
    public void CanReturnsTrueForSuccessfulExpression()
    {
        Assert.Equal(HclValue.True, Evaluate("can(upper(\"a\"))"));
    }

    [Theory]
    [InlineData("can(fail())")]
    [InlineData("can(x.missing)")]
    [InlineData("can(x[5])")]
    [InlineData("can(1 + \"a\")")]
    public void CanReturnsFalseForEvaluationErrors(string expression)
    {
        var result = Evaluate(
            expression,
            variables: ("x", HclValue.FromObject(new Dictionary<string, HclValue> { ["present"] = HclValue.True })));

        Assert.Equal(HclValue.False, result);
    }

    [Fact]
    public void CanOfUnknownIsUnknown()
    {
        var result = Evaluate("can(something_else(1))");

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void TryReturnsFirstSuccessfulArgument()
    {
        var result = Evaluate("try(fail(), x.missing, \"fallback\", \"later\")", variables: ("x", HclValue.Null));

        Assert.Equal(HclValue.FromString("fallback"), result);
    }

    [Fact]
    public void TryFailsWhenEveryArgumentFails()
    {
        Assert.Throws<InvalidOperationException>(() => Evaluate("try(fail(), fail())"));
    }

    [Fact]
    public void TryDoesNotEvaluateLaterArgumentsAfterSuccess()
    {
        var result = Evaluate("try(\"first\", fail())");

        Assert.Equal(HclValue.FromString("first"), result);
        Assert.Empty(_resolver.Calls);
    }

    [Fact]
    public void CanAndTryWorkWithoutAResolver()
    {
        var options = new HclEvaluatorOptions();

        Assert.Equal(HclValue.False, Evaluate("can(x.y)", options, ("x", HclValue.Null)));
        Assert.Equal(HclValue.FromNumber(1), Evaluate("try(x.y, 1)", options, ("x", HclValue.Null)));
    }

    [Fact]
    public void FunctionsWithoutResolverStayUnknown()
    {
        var result = Evaluate("upper(\"a\")", new HclEvaluatorOptions());

        Assert.Equal(HclValueType.Unknown, result.Type);
    }
}
