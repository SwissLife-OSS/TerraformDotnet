using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Tests.Common;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.RoundTrip;

/// <summary>
/// The AST has no node for parentheses, so the emitter has to add the ones that keep the
/// meaning of an expression. Each test emits an expression, parses it back and checks that the
/// tree (and, where it can be evaluated, the value) is unchanged.
/// </summary>
public class HclParenthesesTests
{
    private static readonly HclBinaryOperator[] Arithmetic =
    [
        HclBinaryOperator.Add,
        HclBinaryOperator.Subtract,
        HclBinaryOperator.Multiply,
        HclBinaryOperator.Divide,
        HclBinaryOperator.Modulo,
    ];

    private static readonly HclBinaryOperator[] Logical =
    [
        HclBinaryOperator.And,
        HclBinaryOperator.Or,
        HclBinaryOperator.Equal,
        HclBinaryOperator.NotEqual,
    ];

    public static TheoryData<string, string> SourceCases => new()
    {
        { "(a + b) * 2", "(a + b) * 2" },
        { "a * (b + 2)", "a * (b + 2)" },
        { "a - (b - c)", "a - (b - c)" },
        { "a / (b * c)", "a / (b * c)" },
        { "(a - b) - c", "a - b - c" },
        { "a + (b * c)", "a + b * c" },
        { "(a || b) && c", "(a || b) && c" },
        { "a || (b && c)", "a || b && c" },
        { "a == (b == c)", "a == (b == c)" },
        { "(a < b) == (c < d)", "a < b == c < d" },
        { "!(a && b)", "!(a && b)" },
        { "-(a + b)", "-(a + b)" },
        { "!(a ? b : c)", "!(a ? b : c)" },
        { "-(a.b)", "-a.b" },
        { "(-a).b", "(-a).b" },
        { "(a + b).c", "(a + b).c" },
        { "(a + b)[0]", "(a + b)[0]" },
        { "(a ? b : c)[*].id", "(a ? b : c)[*].id" },
        { "(a ? b : c).*.id", "(a ? b : c).*.id" },
        { "(a ? b : c) + 1", "(a ? b : c) + 1" },
        { "1 + (a ? b : c)", "1 + (a ? b : c)" },
        { "(a ? b : c) ? d : e", "(a ? b : c) ? d : e" },
        { "a ? b : (c ? d : e)", "a ? b : c ? d : e" },
        { "a ? (b ? c : d) : e", "a ? b ? c : d : e" },
        { "(a + b) ? c : d", "a + b ? c : d" },
        { "f((a + b) * c, (d))", "f((a + b) * c, d)" },
        { "[for x in (a + b) : (x - 1) * 2]", "[for x in a + b : (x - 1) * 2]" },
        { "[for x in (a ? b : c) : x]", "[for x in a ? b : c : x]" },
        { "[for x in ((a ? b : c) + d) : x]", "[for x in(a ? b : c) + d : x]" },
        { "{ for k, v in (a + b).c : k => v }", "{ for k, v in(a + b).c : k => v }" },
        { "[for x in a : x if (x ? b : c) == d]", "[for x in a : x if(x ? b : c) == d]" },
        { "{ for k, v in a : k => -v }", "{ for k, v in a : k => - v }" },
        { "{ for k, v in a : k => -v.c - 1 }", "{ for k, v in a : k => - v.c - 1 }" },
        { "{ for k, v in a : k => !-v }", "{ for k, v in a : k => !-v }" },
        { "{ for k, v in a : -k => v }", "{ for k, v in a : -k => v }" },
        { "[for x in -a : x]", "[for x in - a : x]" },
        { "[for x in a : -x if -x > b]", "[for x in a : -x if - x > b]" },
    };

    [Theory]
    [MemberData(nameof(SourceCases))]
    public void Emit_ParsedExpression_KeepsRequiredParentheses(string source, string expected)
    {
        HclExpression parsed = ParseExpression(source);

        string emitted = Emit(parsed);

        Assert.Equal(expected, emitted);
        Assert.Equal(Shape(parsed), Shape(ParseExpression(emitted)));
    }

    [Fact]
    public void Emit_ArithmeticOperatorPairs_KeepTreeAndValue()
    {
        foreach (HclBinaryOperator outer in Arithmetic)
        {
            foreach (HclBinaryOperator inner in Arithmetic)
            {
                AssertRoundTrip(Binary(outer, Binary(inner, Num(7), Num(3)), Num(2)), evaluate: true);
                AssertRoundTrip(Binary(outer, Num(7), Binary(inner, Num(3), Num(2))), evaluate: true);
            }

            AssertRoundTrip(Negate(Binary(outer, Num(7), Num(3))), evaluate: true);
            AssertRoundTrip(Binary(outer, Negate(Num(7)), Num(3)), evaluate: true);
            AssertRoundTrip(Binary(outer, Conditional(Bool(true), Num(7), Num(5)), Num(3)), evaluate: true);
            AssertRoundTrip(Binary(outer, Num(3), Conditional(Bool(false), Num(7), Num(5))), evaluate: true);
        }
    }

    [Fact]
    public void Emit_LogicalOperatorPairs_KeepTreeAndValue()
    {
        foreach (HclBinaryOperator outer in Logical)
        {
            foreach (HclBinaryOperator inner in Logical)
            {
                foreach (bool a in new[] { true, false })
                {
                    AssertRoundTrip(Binary(outer, Binary(inner, Bool(a), Bool(false)), Bool(true)), evaluate: true);
                    AssertRoundTrip(Binary(outer, Bool(a), Binary(inner, Bool(false), Bool(true))), evaluate: true);
                }
            }

            AssertRoundTrip(Not(Binary(outer, Bool(true), Bool(false))), evaluate: true);
        }

        AssertRoundTrip(
            Binary(HclBinaryOperator.Equal, Binary(HclBinaryOperator.LessThan, Num(1), Num(2)), Binary(HclBinaryOperator.GreaterEqual, Num(3), Num(4))),
            evaluate: true);
    }

    [Fact]
    public void Emit_ConditionalPositions_KeepTreeAndValue()
    {
        HclExpression Cond(bool c, double t, double f) => Conditional(Bool(c), Num(t), Num(f));

        AssertRoundTrip(Conditional(Conditional(Bool(true), Bool(false), Bool(true)), Num(1), Num(2)), evaluate: true);
        AssertRoundTrip(Conditional(Bool(false), Num(1), Cond(true, 2, 3)), evaluate: true);
        AssertRoundTrip(Conditional(Bool(true), Cond(false, 2, 3), Num(1)), evaluate: true);
        AssertRoundTrip(Not(Conditional(Bool(true), Bool(false), Bool(true))), evaluate: true);
        AssertRoundTrip(Negate(Cond(true, 2, 3)), evaluate: true);
    }

    [Fact]
    public void Emit_OperationAsTraversalSource_KeepsTree()
    {
        HclExpression[] sources =
        [
            Binary(HclBinaryOperator.Add, Var("a"), Var("b")),
            Negate(Var("a")),
            Not(Var("a")),
            Conditional(Var("a"), Var("b"), Var("c")),
        ];

        foreach (HclExpression source in sources)
        {
            AssertRoundTrip(new HclAttributeAccessExpression { Source = source, Name = "id" }, evaluate: false);
            AssertRoundTrip(new HclIndexExpression { Collection = source, Index = Num(0) }, evaluate: false);

            var splat = new HclSplatExpression { Source = source, IsFullSplat = true };
            splat.Traversal.Add(new HclAttributeAccessExpression { Source = Var("_"), Name = "id" });
            AssertRoundTrip(splat, evaluate: false);
        }
    }

    [Fact]
    public void Emit_RandomExpressionTrees_KeepTree()
    {
        var random = new Random(42);

        for (int i = 0; i < 2000; i++)
        {
            AssertRoundTrip(RandomExpression(random, depth: 4), evaluate: false);
        }
    }

    private static HclExpression RandomExpression(Random random, int depth)
    {
        if (depth == 0)
        {
            return random.Next(2) == 0 ? Var("v" + random.Next(5)) : Num(random.Next(1, 9));
        }

        return random.Next(7) switch
        {
            0 => Negate(RandomExpression(random, depth - 1)),
            1 => Not(RandomExpression(random, depth - 1)),
            2 => Conditional(RandomExpression(random, depth - 1), RandomExpression(random, depth - 1), RandomExpression(random, depth - 1)),
            3 => new HclAttributeAccessExpression { Source = RandomExpression(random, depth - 1), Name = "p" },
            4 => new HclIndexExpression { Collection = RandomExpression(random, depth - 1), Index = RandomExpression(random, depth - 1) },
            _ => Binary(
                (HclBinaryOperator)Enum.GetValues<HclBinaryOperator>().GetValue(random.Next(Enum.GetValues<HclBinaryOperator>().Length))!,
                RandomExpression(random, depth - 1),
                RandomExpression(random, depth - 1)),
        };
    }

    private static void AssertRoundTrip(HclExpression expression, bool evaluate)
    {
        string emitted = Emit(expression);
        HclExpression reparsed = ParseExpression(emitted);

        Assert.True(Shape(expression) == Shape(reparsed), $"'{emitted}' parsed as {Shape(reparsed)}, expected {Shape(expression)}");

        if (evaluate)
        {
            var evaluator = new HclEvaluator();
            Assert.Equal(
                evaluator.Evaluate(expression, new HclEvaluationContext()),
                evaluator.Evaluate(reparsed, new HclEvaluationContext()));
        }
    }

    /// <summary>Renders the tree fully parenthesized, so two trees compare equal only if they have the same structure.</summary>
    private static string Shape(HclExpression expression) => expression switch
    {
        HclBinaryExpression b => $"({Shape(b.Left)} {b.Operator} {Shape(b.Right)})",
        HclUnaryExpression u => $"({u.Operator} {Shape(u.Operand)})",
        HclConditionalExpression c => $"({Shape(c.Condition)} ? {Shape(c.TrueResult)} : {Shape(c.FalseResult)})",
        HclAttributeAccessExpression a => $"{Shape(a.Source)}.{a.Name}",
        HclIndexExpression i => $"{Shape(i.Collection)}[{Shape(i.Index)}]",
        HclSplatExpression s => $"{Shape(s.Source)}{(s.IsFullSplat ? "[*]" : ".*")}{string.Concat(s.Traversal.Select(t => t is HclAttributeAccessExpression a ? "." + a.Name : "[" + Shape(((HclIndexExpression)t).Index) + "]"))}",
        HclFunctionCallExpression f => $"{f.Name}({string.Join(", ", f.Arguments.Select(Shape))})",
        HclForExpression f => $"[for {f.KeyVariable} in {Shape(f.Collection)} : {Shape(f.ValueExpression)}]",
        HclVariableExpression v => v.Name,
        HclLiteralExpression l => $"{l.Kind}:{l.Value}",
        _ => expression.GetType().Name,
    };

    private static HclBinaryExpression Binary(HclBinaryOperator op, HclExpression left, HclExpression right)
        => new() { Operator = op, Left = left, Right = right };

    private static HclUnaryExpression Negate(HclExpression operand)
        => new() { Operator = HclUnaryOperator.Negate, Operand = operand };

    private static HclUnaryExpression Not(HclExpression operand)
        => new() { Operator = HclUnaryOperator.Not, Operand = operand };

    private static HclConditionalExpression Conditional(HclExpression condition, HclExpression whenTrue, HclExpression whenFalse)
        => new() { Condition = condition, TrueResult = whenTrue, FalseResult = whenFalse };

    private static HclVariableExpression Var(string name) => new() { Name = name };

    private static HclLiteralExpression Num(double value)
        => new() { Kind = HclLiteralKind.Number, Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private static HclLiteralExpression Bool(bool value)
        => new() { Kind = HclLiteralKind.Bool, Value = value ? "true" : "false" };

    private static HclExpression ParseExpression(string source) => HclExpression.Parse(source);

    private static string Emit(HclExpression expression)
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute { Name = "x", Value = expression });

        string text = EmitFile(file);

        return text["x = ".Length..^1];
    }

    private static string EmitFile(HclFile file)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        new HclFileEmitter(writer).Emit(file);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Checks against the real <c>terraform fmt</c> running in a container.</summary>
    [Collection(TerraformFmtCollection.Name)]
    public sealed class AgainstTerraformFmt(TerraformFmtResource terraform)
    {
        [Fact]
        public async Task Emit_NegativeNumberAfterArrow_IsSpacedLikeTerraformFmt()
        {
            var forExpr = new HclForExpression
            {
                KeyVariable = "k",
                ValueVariable = "v",
                Collection = new HclVariableExpression { Name = "a" },
                KeyExpression = new HclVariableExpression { Name = "k" },
                ValueExpression = new HclLiteralExpression { Kind = HclLiteralKind.Number, Value = "-5" },
                IsObjectFor = true,
            };

            string emitted = Emit(forExpr);

            Assert.Equal("{ for k, v in a : k => - 5 }", emitted);
            string file = $"x = {emitted}\n";
            Assert.Equal(file, await terraform.FormatAsync(file));
        }

        [Fact]
        public async Task Emit_ParsedExpressions_AreStableUnderTerraformFmt()
        {
            var hcl = new StringBuilder();
            int i = 0;

            foreach (object[] row in SourceCases)
            {
                hcl.Append("x").Append(i++).Append(" = ").Append(Emit(ParseExpression((string)row[0]))).Append('\n');
            }

            string text = hcl.ToString();
            string emitted = EmitFile(HclFileParser.Parse(Encoding.UTF8.GetBytes(text)));

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
        }
    }
}
