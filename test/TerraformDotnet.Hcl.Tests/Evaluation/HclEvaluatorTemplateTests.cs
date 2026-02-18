using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorTemplateTests
{
    private readonly HclEvaluator _evaluator = new();

    [Fact]
    public void SimpleInterpolationWithKnownVariable()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("name", HclValue.FromString("world"));

        // Template: "hello ${name}" — parts: ["hello ", var(name)]
        var template = new HclTemplateExpression { RawContent = "hello ${name}" };
        template.Parts.Add(new HclLiteralExpression { Value = "hello ", Kind = HclLiteralKind.String });
        template.Parts.Add(new HclVariableExpression { Name = "name" });

        var result = _evaluator.Evaluate(template, ctx);

        Assert.Equal(HclValueType.String, result.Type);
        Assert.Equal("hello world", result.StringValue);
    }

    [Fact]
    public void MultipleInterpolations()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("a", HclValue.FromString("hello"));
        ctx.SetVariable("b", HclValue.FromString("world"));

        // "${a}-${b}" → parts: [var(a), "-", var(b)]
        var template = new HclTemplateExpression { RawContent = "${a}-${b}" };
        template.Parts.Add(new HclVariableExpression { Name = "a" });
        template.Parts.Add(new HclLiteralExpression { Value = "-", Kind = HclLiteralKind.String });
        template.Parts.Add(new HclVariableExpression { Name = "b" });

        var result = _evaluator.Evaluate(template, ctx);

        Assert.Equal("hello-world", result.StringValue);
    }

    [Fact]
    public void TemplateWithUnknownFunctionCallPropagates()
    {
        // "${func()}" → parts: [func()]
        var template = new HclTemplateExpression { RawContent = "${func()}" };
        template.Parts.Add(new HclFunctionCallExpression { Name = "func" });

        var result = _evaluator.Evaluate(template, _evaluator is not null ? new HclEvaluationContext() : new HclEvaluationContext());

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void TemplateWithNoPartsReturnsRawContent()
    {
        var template = new HclTemplateExpression { RawContent = "plain string" };

        var result = _evaluator.Evaluate(template, new HclEvaluationContext());

        Assert.Equal("plain string", result.StringValue);
    }

    [Fact]
    public void TemplateInterpolatesNumbers()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("count", HclValue.FromNumber(42));

        // "items: ${count}" → parts: ["items: ", var(count)]
        var template = new HclTemplateExpression { RawContent = "items: ${count}" };
        template.Parts.Add(new HclLiteralExpression { Value = "items: ", Kind = HclLiteralKind.String });
        template.Parts.Add(new HclVariableExpression { Name = "count" });

        var result = _evaluator.Evaluate(template, ctx);

        Assert.Equal("items: 42", result.StringValue);
    }

    [Fact]
    public void TemplateInterpolatesBooleans()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("flag", HclValue.FromBool(true));

        var template = new HclTemplateExpression { RawContent = "enabled: ${flag}" };
        template.Parts.Add(new HclLiteralExpression { Value = "enabled: ", Kind = HclLiteralKind.String });
        template.Parts.Add(new HclVariableExpression { Name = "flag" });

        var result = _evaluator.Evaluate(template, ctx);

        Assert.Equal("enabled: true", result.StringValue);
    }

    [Fact]
    public void TemplateWrapUnwrapsToInnerType()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("x", HclValue.FromNumber(42));

        // "${x}" with wrap → unwraps to number
        var wrap = new HclTemplateWrapExpression
        {
            Wrapped = new HclVariableExpression { Name = "x" },
        };

        var result = _evaluator.Evaluate(wrap, ctx);

        Assert.Equal(HclValueType.Number, result.Type);
        Assert.Equal(42.0, result.NumberValue);
    }

    [Fact]
    public void TemplateWrapWithBoolUnwraps()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("enabled", HclValue.FromBool(true));

        var wrap = new HclTemplateWrapExpression
        {
            Wrapped = new HclVariableExpression { Name = "enabled" },
        };

        var result = _evaluator.Evaluate(wrap, ctx);

        Assert.Equal(HclValueType.Bool, result.Type);
        Assert.True(result.BoolValue);
    }
}
