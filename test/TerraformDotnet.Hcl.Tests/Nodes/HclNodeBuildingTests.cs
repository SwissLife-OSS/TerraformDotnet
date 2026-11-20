using System.Globalization;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Tests.Nodes;

/// <summary>
/// Tests for building HCL trees in code and writing them as text.
/// </summary>
public sealed class HclNodeBuildingTests
{
    // ── Literal factories ───────────────────────────────────────

    [Fact]
    public void StringLiteralEscapesQuotesBackslashesAndControlCharacters()
    {
        var literal = HclLiteralExpression.String("say \"hi\" \\ tab\t\n");

        Assert.Equal(HclLiteralKind.String, literal.Kind);
        Assert.Equal("\"say \\\"hi\\\" \\\\ tab\\t\\n\"", literal.ToHcl());
    }

    [Fact]
    public void StringLiteralKeepsTemplateSequencesLikeParsedLiterals()
    {
        var literal = HclLiteralExpression.String("${var.x} and $${literal}");

        Assert.Equal("\"${var.x} and $${literal}\"", literal.ToHcl());
    }

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(0.1d, "0.1")]
    [InlineData(-2.5d, "-2.5")]
    [InlineData(1e20d, "100000000000000000000")]
    [InlineData(1.5e-7d, "0.00000015")]
    public void DoubleNumberLiteralIsPlainDecimal(double value, string expected)
    {
        var literal = HclLiteralExpression.Number(value);

        Assert.Equal(HclLiteralKind.Number, literal.Kind);
        Assert.Equal(expected, literal.Value);
        Assert.Equal(expected, literal.ToHcl());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DoubleNumberLiteralRejectsNonFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HclLiteralExpression.Number(value));
    }

    [Fact]
    public void NumberLiteralsIgnoreTheCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-CH");

        try
        {
            Assert.Equal("1.5", HclLiteralExpression.Number(1.5d).Value);
            Assert.Equal("1.50", HclLiteralExpression.Number(1.50m).Value);
            Assert.Equal("-1234567", HclLiteralExpression.Number(-1234567).Value);
            Assert.Equal("9223372036854775807", HclLiteralExpression.Number(long.MaxValue).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void BoolAndNullLiteralsMatchParsedLiterals()
    {
        var parsedTrue = Assert.IsType<HclLiteralExpression>(HclExpression.Parse("true"));
        var parsedNull = Assert.IsType<HclLiteralExpression>(HclExpression.Parse("null"));

        HclLiteralExpression builtTrue = HclLiteralExpression.Bool(true);
        HclLiteralExpression builtNull = HclLiteralExpression.Null();

        Assert.Equal((parsedTrue.Kind, parsedTrue.Value), (builtTrue.Kind, builtTrue.Value));
        Assert.Equal((parsedNull.Kind, parsedNull.Value), (builtNull.Kind, builtNull.Value));
        Assert.Equal("false", HclLiteralExpression.Bool(false).ToHcl());
        Assert.Equal("null", builtNull.ToHcl());
    }

    // ── Object keys ─────────────────────────────────────────────

    [Theory]
    [InlineData("name")]
    [InlineData("_private")]
    [InlineData("kebab-case")]
    [InlineData("a1")]
    [InlineData("forward")]
    [InlineData("nullable")]
    public void AddWritesIdentifierKeysBare(string key)
    {
        var obj = new HclObjectExpression();

        HclObjectElement element = obj.Add(key, HclLiteralExpression.Number(1));

        var variable = Assert.IsType<HclVariableExpression>(element.Key);
        Assert.Equal(key, variable.Name);
        Assert.Same(element, Assert.Single(obj.Elements));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("-abc")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("for")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("grüezi")]
    [InlineData("say \"hi\"")]
    [InlineData("${var.x}")]
    public void AddQuotesKeysThatAreNotPlainIdentifiers(string key)
    {
        var obj = new HclObjectExpression();

        HclObjectElement element = obj.Add(key, HclLiteralExpression.Number(1));

        var literal = Assert.IsType<HclLiteralExpression>(element.Key);
        Assert.Equal(HclLiteralKind.String, literal.Kind);
        Assert.Equal(key, literal.Value);
    }

    [Fact]
    public void AddedKeysRoundTripThroughText()
    {
        string[] keys = ["for", "name", "a.b", "true", "_x", "say \"hi\"", "null", "kebab-case", "1abc", "${var.x}"];
        var obj = new HclObjectExpression();

        foreach (string key in keys)
        {
            obj.Add(key, HclLiteralExpression.String(key));
        }

        var parsed = Assert.IsType<HclObjectExpression>(HclExpression.Parse(obj.ToHcl()));

        Assert.Equal(keys, parsed.Elements.Select(e => KeyText(e.Key)));
        Assert.Equal(keys, parsed.Elements.Select(e => ((HclLiteralExpression)e.Value).Value));
    }

    [Fact]
    public void AddRejectsNullArguments()
    {
        var obj = new HclObjectExpression();

        Assert.Throws<ArgumentNullException>(() => obj.Add(null!, HclLiteralExpression.Null()));
        Assert.Throws<ArgumentNullException>(() => obj.Add("x", null!));
    }

    // ── Comments ────────────────────────────────────────────────

    [Fact]
    public void CommentFactoriesPutOneSpaceAfterTheMarker()
    {
        var file = new HclFile
        {
            Body =
            {
                Attributes =
                {
                    Numbered("a", 1, HclComment.Hash("hash")),
                    Numbered("bb", 2, HclComment.Line("line")),
                    Numbered("c", 3, HclComment.Block("block")),
                },
            },
        };

        Assert.Equal("a  = 1 # hash\nbb = 2 // line\nc  = 3 /* block */\n", file.ToHcl());
    }

    [Fact]
    public void EmptyLineCommentHasNoTrailingSpace()
    {
        Assert.Equal(string.Empty, HclComment.Hash(string.Empty).Text);
    }

    [Fact]
    public void BlockCommentMaySpanSeveralLines()
    {
        HclComment comment = HclComment.Block("first\nsecond");

        Assert.Equal(" first\nsecond ", comment.Text);
        Assert.Equal(HclCommentStyle.Block, comment.Style);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void LineCommentFactoriesRejectLineBreaks(string text)
    {
        Assert.Throws<ArgumentException>(() => HclComment.Hash(text));
        Assert.Throws<ArgumentException>(() => HclComment.Line(text));
    }

    [Fact]
    public void BlockCommentFactoryRejectsTheEndMarker()
    {
        Assert.Throws<ArgumentException>(() => HclComment.Block("a */ b"));
    }

    [Theory]
    [InlineData(HclCommentStyle.Hash, " a\nb = 2")]
    [InlineData(HclCommentStyle.Line, " a\rb = 2")]
    [InlineData(HclCommentStyle.Block, " a */ b = 2 /* ")]
    public void EmitterRejectsCommentTextThatWouldEndTheComment(HclCommentStyle style, string text)
    {
        var file = new HclFile
        {
            Body =
            {
                Attributes =
                {
                    new HclAttribute
                    {
                        Name = "a",
                        Value = HclLiteralExpression.Number(1),
                        TrailingComment = new HclComment { Text = text, Style = style },
                    },
                },
            },
        };

        Assert.Throws<ArgumentException>(file.ToHcl);
    }

    // ── ToHcl ───────────────────────────────────────────────────

    [Fact]
    public void EmptyFileIsEmptyText()
    {
        Assert.Equal(string.Empty, new HclFile().ToHcl());
    }

    [Fact]
    public void FileToHclMatchesTerraformFmtLayout()
    {
        var tags = new HclObjectExpression();
        tags.Add("env", HclLiteralExpression.String("prod"));
        tags.Add("cost.center", HclLiteralExpression.String("42"));

        var block = new HclBlock { Type = "module", Labels = { "kv" } };
        block.Body.Attributes.Add(new HclAttribute { Name = "source", Value = HclLiteralExpression.String("./kv") });
        block.Body.Attributes.Add(new HclAttribute { Name = "count", Value = HclLiteralExpression.Number(1) });
        block.Body.Attributes.Add(new HclAttribute { Name = "tags", Value = tags });

        string hcl = new HclFile { Body = { Blocks = { block } } }.ToHcl();

        Assert.Equal(
            "module \"kv\" {\n" +
            "  source = \"./kv\"\n" +
            "  count  = 1\n" +
            "  tags = {\n" +
            "    env           = \"prod\"\n" +
            "    \"cost.center\" = \"42\"\n" +
            "  }\n" +
            "}\n",
            hcl);
    }

    [Fact]
    public void ExpressionToHclIsLaidOutAsAnAttributeValueWithoutTrailingLineBreak()
    {
        var obj = new HclObjectExpression();
        obj.Add("a", HclLiteralExpression.Number(1));
        obj.Add("long_name", new HclTupleExpression { Elements = { HclLiteralExpression.Bool(true) } });

        Assert.Equal("{\n  a         = 1\n  long_name = [true]\n}", obj.ToHcl());
    }

    [Fact]
    public void ExpressionToHclRoundTripsParsedExpressions()
    {
        const string text = "merge(var.tags, { for k, v in local.extra : k => v if v != null })";

        Assert.Equal(text, HclExpression.Parse(text).ToHcl());
    }

    private static HclAttribute Numbered(string name, int value, HclComment comment) =>
        new() { Name = name, Value = HclLiteralExpression.Number(value), TrailingComment = comment };

    private static string? KeyText(HclExpression key) => key switch
    {
        HclVariableExpression variable => variable.Name,
        HclLiteralExpression literal => literal.Value,
        _ => throw new InvalidOperationException($"Unexpected key node {key.GetType().Name}."),
    };
}
