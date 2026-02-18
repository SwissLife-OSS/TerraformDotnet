using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Evaluation;

public sealed class HclEvaluatorFunctionTests
{
    private readonly HclEvaluator _evaluator = new();
    private readonly HclEvaluationContext _context = new();

    [Fact]
    public void FunctionCallReturnsUnknown()
    {
        var expr = new HclFunctionCallExpression { Name = "length" };
        expr.Arguments.Add(new HclLiteralExpression { Value = "hello", Kind = HclLiteralKind.String });

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Equal("length", result.UnknownSource);
    }

    [Fact]
    public void FunctionCallPreservesArguments()
    {
        var expr = new HclFunctionCallExpression { Name = "concat" };
        expr.Arguments.Add(new HclLiteralExpression { Value = "a", Kind = HclLiteralKind.String });
        expr.Arguments.Add(new HclLiteralExpression { Value = "b", Kind = HclLiteralKind.String });

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Equal("concat", result.UnknownSource);
        Assert.Equal(2, result.UnknownArgs.Count);
        Assert.Equal("a", result.UnknownArgs[0].StringValue);
        Assert.Equal("b", result.UnknownArgs[1].StringValue);
    }

    [Fact]
    public void FunctionResultUsedInExpressionPropagatesUnknown()
    {
        // length("hello") + 1 → unknown
        var funcCall = new HclFunctionCallExpression { Name = "length" };
        funcCall.Arguments.Add(new HclLiteralExpression { Value = "hello", Kind = HclLiteralKind.String });

        var expr = new HclBinaryExpression
        {
            Left = funcCall,
            Operator = HclBinaryOperator.Add,
            Right = new HclLiteralExpression { Value = "1", Kind = HclLiteralKind.Number },
        };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
    }

    [Fact]
    public void FunctionWithNoArgs()
    {
        var expr = new HclFunctionCallExpression { Name = "timestamp" };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Equal("timestamp", result.UnknownSource);
        Assert.Empty(result.UnknownArgs);
    }

    [Fact]
    public void FunctionWithVariableArgs()
    {
        var ctx = new HclEvaluationContext();
        ctx.SetVariable("items", HclValue.FromTuple([
            HclValue.FromString("a"),
            HclValue.FromString("b"),
        ]));

        var expr = new HclFunctionCallExpression { Name = "join" };
        expr.Arguments.Add(new HclLiteralExpression { Value = ",", Kind = HclLiteralKind.String });
        expr.Arguments.Add(new HclVariableExpression { Name = "items" });

        var result = _evaluator.Evaluate(expr, ctx);

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Equal("join", result.UnknownSource);
        Assert.Equal(2, result.UnknownArgs.Count);
        Assert.Equal(",", result.UnknownArgs[0].StringValue);
        Assert.Equal(HclValueType.Tuple, result.UnknownArgs[1].Type);
    }

    [Fact]
    public void FunctionInConditionalBranchPropagatesUnknown()
    {
        // true ? cidrsubnet(...) : "" → the function still returns unknown
        var funcCall = new HclFunctionCallExpression { Name = "cidrsubnet" };
        funcCall.Arguments.Add(new HclLiteralExpression { Value = "10.0.0.0/16", Kind = HclLiteralKind.String });

        var expr = new HclConditionalExpression
        {
            Condition = new HclLiteralExpression { Value = "true", Kind = HclLiteralKind.Bool },
            TrueResult = funcCall,
            FalseResult = new HclLiteralExpression { Value = "", Kind = HclLiteralKind.String },
        };

        var result = _evaluator.Evaluate(expr, _context);

        Assert.Equal(HclValueType.Unknown, result.Type);
        Assert.Equal("cidrsubnet", result.UnknownSource);
    }
}
