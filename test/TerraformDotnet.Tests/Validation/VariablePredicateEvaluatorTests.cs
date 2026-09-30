using TerraformDotnet.Validation;

namespace TerraformDotnet.Tests.Validation;

public class VariablePredicateEvaluatorTests
{
    private static readonly TerraformLiteral Gold = TerraformLiteral.FromString("gold");

    private static Dictionary<string, TerraformLiteral> Values(params (string Name, TerraformLiteral Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value);

    private static ComparePredicate Compare(string variable, CompareOperator op, TerraformLiteral value) =>
        new(variable, op, value);

    // ── Comparisons ─────────────────────────────────────────────

    [Theory]
    [InlineData("gold", CompareOperator.Equal, true)]
    [InlineData("silver", CompareOperator.Equal, false)]
    [InlineData("gold", CompareOperator.NotEqual, false)]
    [InlineData("silver", CompareOperator.NotEqual, true)]
    public void StringEquality(string actual, CompareOperator op, bool expected)
    {
        var result = VariablePredicateEvaluator.Evaluate(
            Compare("sla", op, Gold),
            Values(("sla", TerraformLiteral.FromString(actual))));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MissingVariableIsUnknown()
    {
        Assert.Null(VariablePredicateEvaluator.Evaluate(
            Compare("sla", CompareOperator.Equal, Gold),
            Values()));
    }

    [Fact]
    public void NullVariableIsKnownAndNotEqualToAnyValue()
    {
        var values = Values(("sla", TerraformLiteral.Null));

        Assert.False(VariablePredicateEvaluator.Evaluate(Compare("sla", CompareOperator.Equal, Gold), values));
        Assert.True(VariablePredicateEvaluator.Evaluate(Compare("sla", CompareOperator.NotEqual, Gold), values));
    }

    [Fact]
    public void ValuesOfDifferentKindsAreNeverEqual()
    {
        var values = Values(("count", TerraformLiteral.FromString("1")));

        Assert.False(VariablePredicateEvaluator.Evaluate(
            Compare("count", CompareOperator.Equal, TerraformLiteral.FromNumber(1)),
            values));
    }

    [Fact]
    public void NumbersAreComparedNumerically()
    {
        var values = Values(("count", TerraformLiteral.FromNumber(1.0m)));

        Assert.True(VariablePredicateEvaluator.Evaluate(
            Compare("count", CompareOperator.Equal, TerraformLiteral.FromNumber(1)),
            values));
    }

    [Theory]
    [InlineData(CompareOperator.LessThan, 2, true)]
    [InlineData(CompareOperator.LessThan, 3, false)]
    [InlineData(CompareOperator.LessThanOrEqual, 3, true)]
    [InlineData(CompareOperator.GreaterThan, 3, false)]
    [InlineData(CompareOperator.GreaterThan, 4, true)]
    [InlineData(CompareOperator.GreaterThanOrEqual, 3, true)]
    public void NumericOrdering(CompareOperator op, int actual, bool expected)
    {
        var predicate = Compare("count", op, TerraformLiteral.FromNumber(3));

        Assert.Equal(expected, VariablePredicateEvaluator.Evaluate(
            predicate,
            Values(("count", TerraformLiteral.FromNumber(actual)))));
    }

    [Fact]
    public void OrderingOnNonNumbersIsUnknown()
    {
        Assert.Null(VariablePredicateEvaluator.Evaluate(
            Compare("count", CompareOperator.GreaterThan, TerraformLiteral.FromNumber(3)),
            Values(("count", TerraformLiteral.FromString("abc")))));

        Assert.Null(VariablePredicateEvaluator.Evaluate(
            Compare("count", CompareOperator.GreaterThan, TerraformLiteral.FromNumber(3)),
            Values(("count", TerraformLiteral.Null))));
    }

    // ── In / IsNull ─────────────────────────────────────────────

    [Theory]
    [InlineData("gold", false, true)]
    [InlineData("silver", false, false)]
    [InlineData("gold", true, false)]
    [InlineData("silver", true, true)]
    public void InPredicate(string actual, bool negated, bool expected)
    {
        var predicate = new InPredicate("sla", [Gold, TerraformLiteral.FromString("platinum")], negated);

        Assert.Equal(expected, VariablePredicateEvaluator.Evaluate(
            predicate,
            Values(("sla", TerraformLiteral.FromString(actual)))));
    }

    [Fact]
    public void InPredicateWithMissingVariableIsUnknown()
    {
        Assert.Null(VariablePredicateEvaluator.Evaluate(new InPredicate("sla", [Gold], false), Values()));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void IsNullPredicate(bool isNull, bool negated, bool expected)
    {
        var value = isNull ? TerraformLiteral.Null : Gold;

        Assert.Equal(expected, VariablePredicateEvaluator.Evaluate(
            new IsNullPredicate("sla", negated),
            Values(("sla", value))));
    }

    [Fact]
    public void IsNullPredicateWithMissingVariableIsUnknown()
    {
        Assert.Null(VariablePredicateEvaluator.Evaluate(new IsNullPredicate("sla", false), Values()));
    }

    // ── Three-valued logic ──────────────────────────────────────

    private static readonly ComparePredicate IsGold = Compare("sla", CompareOperator.Equal, Gold);
    private static readonly ComparePredicate IsEu = Compare("region", CompareOperator.Equal, TerraformLiteral.FromString("eu"));

    [Fact]
    public void AndIsFalseWhenAnyOperandIsFalseEvenIfAnotherIsUnknown()
    {
        var predicate = new AndPredicate([IsGold, IsEu]);

        Assert.False(VariablePredicateEvaluator.Evaluate(predicate, Values(("sla", TerraformLiteral.FromString("silver")))));
    }

    [Fact]
    public void AndIsUnknownWhenNothingIsFalseButSomethingIsUnknown()
    {
        var predicate = new AndPredicate([IsGold, IsEu]);

        Assert.Null(VariablePredicateEvaluator.Evaluate(predicate, Values(("sla", Gold))));
    }

    [Fact]
    public void AndIsTrueWhenAllOperandsAreTrue()
    {
        var predicate = new AndPredicate([IsGold, IsEu]);

        Assert.True(VariablePredicateEvaluator.Evaluate(
            predicate,
            Values(("sla", Gold), ("region", TerraformLiteral.FromString("eu")))));
    }

    [Fact]
    public void OrIsTrueWhenAnyOperandIsTrueEvenIfAnotherIsUnknown()
    {
        var predicate = new OrPredicate([IsGold, IsEu]);

        Assert.True(VariablePredicateEvaluator.Evaluate(predicate, Values(("sla", Gold))));
    }

    [Fact]
    public void OrIsUnknownWhenNothingIsTrueButSomethingIsUnknown()
    {
        var predicate = new OrPredicate([IsGold, IsEu]);

        Assert.Null(VariablePredicateEvaluator.Evaluate(predicate, Values(("sla", TerraformLiteral.FromString("silver")))));
    }

    [Fact]
    public void OrIsFalseWhenAllOperandsAreFalse()
    {
        var predicate = new OrPredicate([IsGold, IsEu]);

        Assert.False(VariablePredicateEvaluator.Evaluate(
            predicate,
            Values(("sla", TerraformLiteral.FromString("silver")), ("region", TerraformLiteral.FromString("us")))));
    }

    [Fact]
    public void NestedPredicatesAreEvaluated()
    {
        var predicate = new OrPredicate([new AndPredicate([IsGold, IsEu]), new IsNullPredicate("sla", false)]);

        Assert.True(VariablePredicateEvaluator.Evaluate(predicate, Values(("sla", TerraformLiteral.Null))));
        Assert.False(VariablePredicateEvaluator.Evaluate(
            predicate,
            Values(("sla", Gold), ("region", TerraformLiteral.FromString("us")))));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => VariablePredicateEvaluator.Evaluate(null!, Values()));
        Assert.Throws<ArgumentNullException>(() => VariablePredicateEvaluator.Evaluate(IsGold, null!));
    }
}
