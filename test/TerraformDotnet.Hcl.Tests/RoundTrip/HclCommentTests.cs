using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Tests.Common;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.RoundTrip;

/// <summary>
/// Where the parser attaches comments and how the emitter writes them back. A comment on the
/// line where a node ends is that node's trailing comment, comments on their own lines lead the
/// next element, and comments before a closing bracket belong to the container.
/// </summary>
public sealed class HclCommentTests
{
    public static TheoryData<string, string> CanonicalCases => new()
    {
        {
            "attribute trailing comments are aligned",
            Lines("a   = 1  # one", "bbb = 22 // two", "c   = 3 /* three */", "d   = 4")
        },
        {
            "object element comments",
            Lines("m = {", "  # lead", "  a   = 1 # one", "  bbb = 2", "  # tail", "}")
        },
        {
            "tuple element comments",
            Lines("t = [", "  # lead", "  1, # one", "  2  # two", "  # tail", "]")
        },
        {
            "function call argument comments",
            Lines("x = merge(", "  # lead", "  local.a, # one", "  local.b", ")")
        },
        {
            "expanded final argument keeps its comment",
            Lines("x = concat(", "  local.a,", "  local.b... # rest", ")")
        },
        {
            "comment inside list of objects",
            Lines("ports = [", "  {", "    port = 80 # http", "  },", "  # second", "  {", "    port = 443", "  }", "]")
        },
        {
            "comment after nested object closer",
            Lines("t = [", "  {", "    a = 1", "  }, # first", "  {", "    a = 2", "  } # last", "]")
        },
        {
            "block trailing and dangling comments",
            Lines("block {", "  a = 1", "  # tail", "} # after")
        },
        {
            "comment after multi-line attribute is aligned",
            Lines("m = {", "  a = 1", "}     # close", "b = 2 # two")
        },
        {
            "empty containers keep their comments",
            Lines("o = {", "  # o", "}", "t = [", "  # t", "]", "c = f(", "  # c", ")")
        },
        {
            "empty block keeps its comment",
            Lines("block {", "  # nothing yet", "}")
        },
        {
            "nested block comments",
            Lines("outer {", "  inner {", "    a = 1 # one", "    # inner tail", "  } # inner", "  # outer tail", "}")
        },
        {
            "block comment style",
            Lines("t = [", "  /* lead */", "  1, /* one */", "  2", "]")
        },
    };

    // ── Parser attachment ───────────────────────────────────

    [Fact]
    public void Parse_CommentOnSameLine_IsTrailingCommentOfAttribute()
    {
        HclFile file = Parse(Lines("a = 1 # one", "# next", "b = 2"));

        HclAttribute a = file.Body.Attributes[0];
        HclAttribute b = file.Body.Attributes[1];
        Assert.Equal(" one", a.TrailingComment?.Text);
        Assert.Empty(a.LeadingComments);
        Assert.Equal(" next", Assert.Single(b.LeadingComments).Text);
        Assert.Null(b.TrailingComment);
    }

    [Fact]
    public void Parse_CommentAfterMultiLineValue_IsTrailingCommentOfAttribute()
    {
        HclFile file = Parse(Lines("m = {", "  a = 1", "} # close", "b = 2"));

        Assert.Equal(" close", file.Body.Attributes[0].TrailingComment?.Text);
        Assert.Empty(file.Body.Attributes[1].LeadingComments);
    }

    [Fact]
    public void Parse_CommentAfterBlockCloser_IsTrailingCommentOfBlock()
    {
        HclFile file = Parse(Lines("block {", "  a = 1", "} # after", "b = 2"));

        Assert.Equal(" after", Assert.Single(file.Body.Blocks).TrailingComment?.Text);
        Assert.Empty(file.Body.Attributes[0].LeadingComments);
    }

    [Fact]
    public void Parse_CommentBeforeBlockCloser_IsDanglingCommentOfBody()
    {
        HclFile file = Parse(Lines("block {", "  a = 1", "  # tail", "}", "b = 2"));

        HclBlock block = Assert.Single(file.Body.Blocks);
        Assert.Equal(" tail", Assert.Single(block.Body.DanglingComments).Text);
        Assert.Null(block.Body.Attributes[0].TrailingComment);
        Assert.Empty(file.Body.Attributes[0].LeadingComments);
    }

    [Fact]
    public void Parse_ObjectComments_AttachToElementsAndCloser()
    {
        HclFile file = Parse(Lines("m = {", "  # lead", "  a = 1 # one", "  b = 2, # two", "  c = 3", "  # tail", "}"));

        var obj = Assert.IsType<HclObjectExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(" lead", Assert.Single(obj.Elements[0].LeadingComments).Text);
        Assert.Equal(" one", obj.Elements[0].TrailingComment?.Text);
        Assert.Equal(" two", obj.Elements[1].TrailingComment?.Text);
        Assert.Empty(obj.Elements[2].LeadingComments);
        Assert.Null(obj.Elements[2].TrailingComment);
        Assert.Equal(" tail", Assert.Single(obj.DanglingComments).Text);
    }

    [Fact]
    public void Parse_TupleComments_AttachToElementsAndCloser()
    {
        HclFile file = Parse(Lines("t = [", "  # lead", "  1, # one", "  2 # two", "  # tail", "]"));

        var tuple = Assert.IsType<HclTupleExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(" lead", Assert.Single(tuple.Elements[0].LeadingComments).Text);
        Assert.Equal(" one", tuple.Elements[0].TrailingComment?.Text);
        Assert.Equal(" two", tuple.Elements[1].TrailingComment?.Text);
        Assert.Equal(" tail", Assert.Single(tuple.DanglingComments).Text);
    }

    [Fact]
    public void Parse_CallComments_AttachToArgumentsAndCloser()
    {
        HclFile file = Parse(Lines("x = f(", "  # lead", "  a, # one", "  b... # rest", "  # tail", ")"));

        var call = Assert.IsType<HclFunctionCallExpression>(file.Body.Attributes[0].Value);
        Assert.True(call.ExpandFinalArgument);
        Assert.Equal(" lead", Assert.Single(call.Arguments[0].LeadingComments).Text);
        Assert.Equal(" one", call.Arguments[0].TrailingComment?.Text);
        Assert.Equal(" rest", call.Arguments[1].TrailingComment?.Text);
        Assert.Equal(" tail", Assert.Single(call.DanglingComments).Text);
    }

    [Fact]
    public void Parse_EmptyContainersWithComments_HaveDanglingComments()
    {
        HclFile file = Parse(Lines("o = {", "  # o", "}", "t = [", "  # t", "]", "c = f(", "  # c", ")", "d = 1"));

        Assert.Equal(" o", Assert.Single(Assert.IsType<HclObjectExpression>(file.Body.Attributes[0].Value).DanglingComments).Text);
        Assert.Equal(" t", Assert.Single(Assert.IsType<HclTupleExpression>(file.Body.Attributes[1].Value).DanglingComments).Text);
        Assert.Equal(" c", Assert.Single(Assert.IsType<HclFunctionCallExpression>(file.Body.Attributes[2].Value).DanglingComments).Text);
        Assert.Empty(file.Body.Attributes[3].LeadingComments);
    }

    [Fact]
    public void Parse_CommentsInNestedContainers_StayInInnerContainer()
    {
        HclFile file = Parse(Lines("t = [", "  {", "    a = 1 # one", "    # inner", "  }, # obj", "  # outer", "]"));

        var tuple = Assert.IsType<HclTupleExpression>(file.Body.Attributes[0].Value);
        var inner = Assert.IsType<HclObjectExpression>(Assert.Single(tuple.Elements));
        Assert.Equal(" one", inner.Elements[0].TrailingComment?.Text);
        Assert.Equal(" inner", Assert.Single(inner.DanglingComments).Text);
        Assert.Equal(" obj", inner.TrailingComment?.Text);
        Assert.Equal(" outer", Assert.Single(tuple.DanglingComments).Text);
    }

    [Fact]
    public void Parse_CommentOnOpeningLine_LeadsFirstElement()
    {
        HclFile file = Parse(Lines("m = { # open", "  a = 1", "}", "block { # header", "  b = 2", "}"));

        var obj = Assert.IsType<HclObjectExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(" open", Assert.Single(obj.Elements[0].LeadingComments).Text);
        Assert.Equal(" header", Assert.Single(file.Body.Blocks[0].Body.Attributes[0].LeadingComments).Text);
    }

    [Fact]
    public void Parse_CommentInsideConditional_IsHoistedToNextAttribute()
    {
        HclFile file = Parse(Lines("a = (", "  # inside", "  b ? 1 : 2", ")", "c = 3"));

        Assert.Null(file.Body.Attributes[0].TrailingComment);
        Assert.Equal(" inside", Assert.Single(file.Body.Attributes[1].LeadingComments).Text);
    }

    [Fact]
    public void Parse_TwoCommentsOnElementLine_SecondLeadsNextElement()
    {
        HclFile file = Parse(Lines("t = [", "  1 /* a */, # b", "  2", "]"));

        var tuple = Assert.IsType<HclTupleExpression>(file.Body.Attributes[0].Value);
        Assert.Equal(" a ", tuple.Elements[0].TrailingComment?.Text);
        Assert.Equal(" b", Assert.Single(tuple.Elements[1].LeadingComments).Text);
    }

    // ── Emit ────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(CanonicalCases))]
    public void Emit_CanonicalInputWithComments_IsIdempotent(string name, string canonical)
    {
        Assert.NotNull(name);
        Assert.Equal(canonical, EmitFile(Parse(canonical)));
    }

    [Theory]
    [InlineData("t = [\n  1, # one\n  2,\n]\n", "t = [\n  1, # one\n  2\n]\n")]
    [InlineData("m = { # open\n  a = 1\n}\n", "m = {\n  # open\n  a = 1\n}\n")]
    [InlineData("block { # header\n  a = 1\n}\n", "block {\n  # header\n  a = 1\n}\n")]
    [InlineData("t = [ # open\n  1,\n]\n", "t = [\n  # open\n  1\n]\n")]
    public void Emit_InputOutsideTheCommentModel_IsNormalized(string source, string expected)
    {
        string emitted = EmitFile(Parse(source));

        Assert.Equal(expected, emitted);
        Assert.Equal(emitted, EmitFile(Parse(emitted)));
    }

    [Fact]
    public void Emit_PreserveCommentsDisabled_KeepsInlineLayout()
    {
        HclFile file = Parse(Lines("t = [", "  1, # one", "  2", "]", "c = f(", "  a, # one", "  b", ")", "o = {", "  # o", "}"));

        string emitted = EmitFile(file, preserveComments: false);

        Assert.Equal(Lines("t = [1, 2]", "c = f(a, b)", "o = {}"), emitted);
    }

    [Fact]
    public void Emit_ProgrammaticTupleElementComment_ExpandsTuple()
    {
        HclExpression first = Num("1");
        first.TrailingComment = Comment(" one");
        var tuple = new HclTupleExpression();
        tuple.Elements.Add(first);
        tuple.Elements.Add(Num("2"));

        string emitted = EmitAttribute("t", tuple);

        Assert.Equal(Lines("t = [", "  1, # one", "  2", "]"), emitted);
    }

    [Fact]
    public void Emit_ProgrammaticCallDanglingComment_ExpandsCall()
    {
        var call = new HclFunctionCallExpression { Name = "f" };
        call.Arguments.Add(Ident("a"));
        call.DanglingComments.Add(Comment(" end"));

        string emitted = EmitAttribute("x", call);

        Assert.Equal(Lines("x = f(", "  a", "  # end", ")"), emitted);
    }

    [Fact]
    public void Emit_HeredocWithTrailingComment_PutsCommentOnNextLine()
    {
        HclExpression lastInTuple = Heredoc("two\n");
        lastInTuple.TrailingComment = Comment(" two");
        HclExpression firstInTuple = Heredoc("one\n");
        firstInTuple.TrailingComment = Comment(" one");
        var tuple = new HclTupleExpression();
        tuple.Elements.Add(firstInTuple);
        tuple.Elements.Add(lastInTuple);

        var obj = new HclObjectExpression();
        obj.Elements.Add(new HclObjectElement { Key = Ident("k"), Value = Heredoc("v\n"), TrailingComment = Comment(" k") });

        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute { Name = "a", Value = Heredoc("a\n"), TrailingComment = Comment(" a") });
        file.Body.Attributes.Add(new HclAttribute { Name = "t", Value = tuple });
        file.Body.Attributes.Add(new HclAttribute { Name = "o", Value = obj });

        string emitted = EmitFile(file);

        Assert.Equal(
            Lines(
                "a = <<EOT",
                "a",
                "EOT",
                "# a",
                "t = [",
                "  <<EOT",
                "one",
                "EOT",
                "  , # one",
                "  <<EOT",
                "two",
                "EOT",
                "  # two",
                "]",
                "o = {",
                "  k = <<EOT",
                "v",
                "EOT",
                "  # k",
                "}"),
            emitted);

        // The parser reads a heredoc without interpolation back as a quoted string, so only the
        // comments can be compared after a second pass.
        string reEmitted = EmitFile(Parse(emitted));
        foreach (string comment in new[] { "# a\n", "# one\n", "# two\n", "# k\n" })
        {
            Assert.Contains(comment, reEmitted, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeepClone_ParsedFileWithComments_EmitsSameText()
    {
        string source = Lines(
            "m = {", "  # lead", "  a = 1 # one", "  # tail", "}",
            "t = [", "  # lead", "  1, # one", "  2", "  # tail", "]",
            "x = f(", "  a, # one", "  b", "  # tail", ")",
            "",
            "block {", "  b = 1", "  # tail", "} # after");
        HclFile file = Parse(source);

        var clone = (HclFile)file.DeepClone();

        Assert.Equal(source, EmitFile(file));
        Assert.Equal(source, EmitFile(clone));
    }

    private static HclComment Comment(string text) => new() { Text = text, Style = HclCommentStyle.Hash };

    private static HclTemplateExpression Heredoc(string content)
        => new() { RawContent = content, IsHeredoc = true, HeredocMarker = "EOT" };

    private static HclLiteralExpression Num(string value) => new() { Kind = HclLiteralKind.Number, Value = value };

    private static HclLiteralExpression Str(string value) => new() { Kind = HclLiteralKind.String, Value = value };

    private static HclVariableExpression Ident(string value) => new() { Name = value };

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string EmitAttribute(string name, HclExpression value)
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute { Name = name, Value = value });

        return EmitFile(file);
    }

    private static string EmitFile(HclFile file, bool preserveComments = true)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        new HclFileEmitter(writer, preserveComments).Emit(file);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static HclFile Parse(string hcl) => HclFileParser.Parse(Encoding.UTF8.GetBytes(hcl));

    /// <summary>Checks against the real <c>terraform fmt</c> running in a container.</summary>
    [Collection(TerraformFmtCollection.Name)]
    public sealed class AgainstTerraformFmt(TerraformFmtResource terraform)
    {
        [Theory]
        [MemberData(nameof(CanonicalCases), MemberType = typeof(HclCommentTests))]
        public async Task Emit_CanonicalInputWithComments_IsTerraformFmtOutput(string name, string canonical)
        {
            Assert.NotNull(name);
            Assert.Equal(canonical, await terraform.FormatAsync(canonical));
        }

        [Fact]
        public async Task Emit_HeredocWithTrailingComment_IsStableUnderTerraformFmt()
        {
            HclExpression element = Heredoc("one\n");
            element.TrailingComment = Comment(" one");
            var tuple = new HclTupleExpression();
            tuple.Elements.Add(element);
            tuple.Elements.Add(Str("two"));

            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "a", Value = Heredoc("a\n"), TrailingComment = Comment(" a") });
            file.Body.Attributes.Add(new HclAttribute { Name = "t", Value = tuple });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
        }
    }
}
