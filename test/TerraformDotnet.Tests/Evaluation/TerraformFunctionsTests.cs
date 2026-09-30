using TerraformDotnet.Evaluation;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Tests.Evaluation;

public class TerraformFunctionsTests
{
    private static readonly HclEvaluator Evaluator = new(new HclEvaluatorOptions
    {
        FunctionResolver = TerraformFunctions.Default,
    });

    private static HclValue Evaluate(string expression)
        => Evaluator.Evaluate(HclExpression.Parse(expression), new HclEvaluationContext());

    [Theory]
    [InlineData("cidrcontains(\"192.168.2.0/20\", \"192.168.2.0\")", true)]
    [InlineData("cidrcontains(\"192.168.2.0/20\", \"192.168.2.0/24\")", true)]
    [InlineData("cidrcontains(\"192.168.2.0/20\", \"192.168.2.0/20\")", true)]
    [InlineData("cidrcontains(\"192.168.2.0/24\", \"192.168.2.0/20\")", false)]
    [InlineData("cidrcontains(\"192.168.2.0/20\", \"10.0.0.0/8\")", false)]
    [InlineData("cidrcontains(\"192.168.2.0/20\", \"2001:db8::1\")", false)]
    [InlineData("cidrcontains(\"2001:db8::/32\", \"2001:db8::1\")", true)]
    [InlineData("cidrcontains(\"2001:db8::/32\", \"2001:db9::/48\")", false)]
    public void CidrContainsChecksMembership(string expression, bool expected)
    {
        Assert.Equal(HclValue.FromBool(expected), Evaluate(expression));
    }

    [Theory]
    [InlineData("cidrcontains(\"10.0.0.0\", \"10.0.0.1\")")]
    [InlineData("cidrcontains(\"10.0.0.0/33\", \"10.0.0.1\")")]
    [InlineData("cidrcontains(\"not-a-cidr\", \"10.0.0.1\")")]
    public void CidrContainsRejectsInvalidInput(string expression)
    {
        Assert.Throws<HclFunctionException>(() => Evaluate(expression));
    }

    [Theory]
    [InlineData("startswith(\"hello\", \"he\")", true)]
    [InlineData("startswith(\"hello\", \"\")", true)]
    [InlineData("startswith(\"hello\", \"hello!\")", false)]
    [InlineData("endswith(\"hello\", \"llo\")", true)]
    [InlineData("endswith(\"hello\", \"\")", true)]
    [InlineData("endswith(\"hello\", \"Hello\")", false)]
    public void StartsAndEndsWithAreOrdinal(string expression, bool expected)
    {
        Assert.Equal(HclValue.FromBool(expected), Evaluate(expression));
    }

    [Theory]
    [InlineData("regex(\"(?m)^b\", \"a\\nb\")")]
    [InlineData("regex(\"(?U)a+\", \"aaa\")")]
    [InlineData("regex(\"(?<=a)b\", \"ab\")")]
    [InlineData("regex(\"(a)\\\\1\", \"aa\")")]
    [InlineData("regex(\"\\\\p{Han}\", \"x\")")]
    [InlineData("replace(\"hello\", \"/(l+)/\", \"[$1]\")")]
    public void UnsupportedRegexFeaturesStayUnknown(string expression)
    {
        Assert.Equal(HclValueType.Unknown, Evaluate(expression).Type);
    }

    [Fact]
    public void CatastrophicRegexBacktrackingStaysUnknown()
    {
        var input = new string('a', 60) + "b";

        var result = Evaluate($"can(regex(\"^(a+)+$\", \"{input}\"))");

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void UnsupportedFunctionsAreNotResolved()
    {
        Assert.False(TerraformFunctions.IsSupported("timestamp"));
        Assert.False(TerraformFunctions.IsSupported("file"));
        Assert.False(TerraformFunctions.IsSupported("uuid"));
        Assert.Equal(HclValueType.Unknown, Evaluate("timestamp()").Type);
        Assert.Equal(HclValueType.Unknown, Evaluate("file(\"/etc/passwd\")").Type);
    }

    [Fact]
    public void SupportedFunctionsAreSortedAndIncludeTheCoreLibrary()
    {
        var names = TerraformFunctions.SupportedFunctions;

        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.All(
            ["contains", "regex", "regexall", "length", "lookup", "cidrsubnet", "format", "join", "toset", "alltrue", "one"],
            name => Assert.Contains(name, names));
    }

    [Fact]
    public void WrongArgumentCountIsARecoverableFunctionError()
    {
        var exception = Assert.Throws<HclFunctionException>(() => Evaluate("length()"));

        Assert.Contains("length", exception.Message, StringComparison.Ordinal);
        Assert.False(Evaluate("can(length())").BoolValue);
    }

    [Fact]
    public void ResultsAreDeterministicAcrossCalls()
    {
        var first = Evaluate("jsonencode({b = [3, 1, 2], a = toset([\"z\", \"y\"])})");
        var second = Evaluate("jsonencode({a = toset([\"y\", \"z\"]), b = [3, 1, 2]})");

        Assert.Equal(first, second);
        Assert.Equal("{\"a\":[\"y\",\"z\"],\"b\":[3,1,2]}", first.StringValue);
    }

    [Fact]
    public void EvaluatingTemplatesUsesTheFunctionLibrary()
    {
        var result = Evaluate("\"prefix-${upper(\\\"abc\\\")}-${length([1, 2])}\"");

        Assert.Equal("prefix-ABC-2", result.StringValue);
    }

    [Fact]
    public void RangeIsBoundedToProtectAgainstExhaustion()
    {
        Assert.False(Evaluate("can(range(100000))").BoolValue);
    }

    [Fact]
    public void ForExpressionsOverHugeCollectionsHitTheIterationLimit()
    {
        var evaluator = new HclEvaluator(new HclEvaluatorOptions
        {
            FunctionResolver = TerraformFunctions.Default,
            MaxIterations = 10,
        });

        Assert.Throws<HclEvaluationLimitException>(
            () => evaluator.Evaluate(HclExpression.Parse("[for i in range(100) : i]"), new HclEvaluationContext()));
    }
}
