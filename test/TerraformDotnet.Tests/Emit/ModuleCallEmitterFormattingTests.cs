using TerraformDotnet.Emit;
using TerraformDotnet.Hcl.Tests.Common;
using TerraformDotnet.Module;

namespace TerraformDotnet.Tests.Emit;

/// <summary>
/// Layout tests for <see cref="ModuleCallEmitter"/> output. The text must already be in
/// <c>terraform fmt</c> canonical form: <c>=</c> signs are aligned only across adjacent
/// single-line assignments, multi-line values are never padded, and trailing comments are aligned.
/// Every layout is also checked against the real <c>terraform fmt</c> running in a container.
/// </summary>
[Collection(TerraformFmtCollection.Name)]
public class ModuleCallEmitterFormattingTests(TerraformFmtResource terraform)
{
    private const string ObjectList = "[\n  {\n    name = \"http\"\n    port = 80\n  },\n  {\n    name = \"https\"\n    port = 443\n  }\n]";

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string Block(params string[] lines) => string.Join("\n", lines);

    private static ModuleCallEmitter EmitterFor(Action<ModuleCallBuilder> configure)
    {
        var builder = new ModuleCallBuilder("svc").Source("./modules/svc");
        configure(builder);

        return new ModuleCallEmitter(builder.Build());
    }

    private static string InputValues(IDictionary<string, InputValue> values, bool compact = false)
        => EmitterFor(_ => { }).EmitInputValues(values, compactSpacing: compact);

    private async Task AssertFmtStableAsync(string hcl)
    {
        string withNewline = hcl.EndsWith('\n') ? hcl : hcl + "\n";
        Assert.Equal(withNewline, await terraform.FormatAsync(withNewline));
    }

    // ── EmitModuleBlock ─────────────────────────────────────────

    [Fact]
    public async Task EmitModuleBlock_MultiLineArgument_IsNotPaddedAndShiftedByBlockIndent()
    {
        var emitter = EmitterFor(b => b.Set("ports", ObjectList).Set("name", "\"x\""));

        string result = emitter.EmitModuleBlock();

        Assert.Equal(
            Block(
                "module \"svc\" {",
                "  source = \"./modules/svc\"",
                "",
                "  ports = [",
                "    {",
                "      name = \"http\"",
                "      port = 80",
                "    },",
                "    {",
                "      name = \"https\"",
                "      port = 443",
                "    }",
                "  ]",
                "  name = \"x\"",
                "}"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitModuleBlock_MultiLineArgumentBetweenSingleLines_SplitsAlignmentRuns()
    {
        var emitter = EmitterFor(b => b
            .Set("a", "1")
            .Set("long_name", "{\n  x = 1\n}")
            .Set("bb", "2")
            .Set("ccc", "3"));

        string result = emitter.EmitModuleBlock();

        Assert.Equal(
            Block(
                "module \"svc\" {",
                "  source = \"./modules/svc\"",
                "",
                "  a = 1",
                "  long_name = {",
                "    x = 1",
                "  }",
                "  bb  = 2",
                "  ccc = 3",
                "}"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitModuleBlock_AdjacentSingleLineArguments_AreAligned()
    {
        var emitter = EmitterFor(b => b.Set("a", "1").Set("long_name", "2"));

        string result = emitter.EmitModuleBlock();

        Assert.Contains("  a         = 1\n  long_name = 2\n", result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitModuleBlock_HeredocArgument_KeepsBodyVerbatim()
    {
        var emitter = EmitterFor(b => b
            .Set("a", "1")
            .Set("script", "<<EOT\n  echo hi\nEOT")
            .Set("bb", "2"));

        string result = emitter.EmitModuleBlock();

        Assert.Equal(
            Block(
                "module \"svc\" {",
                "  source = \"./modules/svc\"",
                "",
                "  a      = 1",
                "  script = <<EOT",
                "  echo hi",
                "EOT",
                "  bb     = 2",
                "}"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitModuleBlock_MultiLineFunctionCallArgument_IsShifted()
    {
        var emitter = EmitterFor(b => b.Set("tags", "merge(\n  var.a,\n  {\n    b = 1\n  }\n)"));

        string result = emitter.EmitModuleBlock();

        Assert.Contains("  tags = merge(\n    var.a,\n    {\n      b = 1\n    }\n  )\n", result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitModuleBlock_MetaArguments_AreAlignedAsOwnGroup()
    {
        var emitter = EmitterFor(b => b
            .Set("name", "\"x\"")
            .ForEach("var.items")
            .DependsOn("aws_vpc.main"));

        string result = emitter.EmitModuleBlock();

        Assert.Equal(
            Block(
                "module \"svc\" {",
                "  source = \"./modules/svc\"",
                "",
                "  name = \"x\"",
                "",
                "  for_each   = var.items",
                "  depends_on = [aws_vpc.main]",
                "}"),
            result);
        await AssertFmtStableAsync(result);
    }

    // ── EmitInputValues ─────────────────────────────────────────

    [Fact]
    public async Task EmitInputValues_MultiLineValue_IsNotPaddedAndBreaksRun()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = "1",
            ["long_name"] = "{\n  x = 1\n}",
            ["bb"] = "2",
            ["ccc"] = "3",
        };

        string result = InputValues(values, compact: true);

        Assert.Equal(
            Lines("a = 1", "long_name = {", "  x = 1", "}", "bb  = 2", "ccc = 3"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_ObjectList_KeepsRelativeIndentation()
    {
        var values = new Dictionary<string, InputValue> { ["ports"] = ObjectList };

        string result = InputValues(values);

        Assert.Equal(
            Lines(
                "ports = [",
                "  {",
                "    name = \"http\"",
                "    port = 80",
                "  },",
                "  {",
                "    name = \"https\"",
                "    port = 443",
                "  }",
                "]"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_DefaultSpacing_DoesNotAlignAcrossBlankLines()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = "1",
            ["long_name"] = "2",
        };

        string result = InputValues(values);

        Assert.Equal(Lines("a = 1", "", "long_name = 2"), result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_CompactSpacing_AlignsAdjacentAssignments()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = "1",
            ["long_name"] = "2",
        };

        string result = InputValues(values, compact: true);

        Assert.Equal(Lines("a         = 1", "long_name = 2"), result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_AdjacentTrailingComments_AreAligned()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = new("1", "first"),
            ["long_name"] = new("\"value\"", "second"),
            ["c"] = "3",
            ["d"] = new("4", "fourth"),
        };

        string result = InputValues(values, compact: true);

        Assert.Equal(
            Lines(
                "a         = 1       # first",
                "long_name = \"value\" # second",
                "c         = 3",
                "d         = 4 # fourth"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public void EmitInputValues_SingleTrailingComment_HasOneSpaceBeforeHash()
    {
        var values = new Dictionary<string, InputValue> { ["a"] = new("1", "note") };

        Assert.Equal(Lines("a = 1 # note"), InputValues(values));
    }

    [Fact]
    public async Task EmitInputValues_CommentOnMultiLineValue_FollowsClosingBracket()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["m"] = new("{\n  x = 1\n}", "map"),
        };

        string result = InputValues(values);

        Assert.Equal(Lines("m = {", "  x = 1", "} # map"), result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_HeredocValue_IsAlignedAndCommentGoesOnOwnLine()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = "1",
            ["text"] = new("<<EOT\nhello\nEOT", "doc"),
            ["bb"] = "2",
        };

        string result = InputValues(values, compact: true);

        Assert.Equal(
            Lines("a    = 1", "text = <<EOT", "hello", "EOT", "# doc", "bb = 2"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_HeredocValue_WithoutComment_StaysAlignedWithNeighbours()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = "1",
            ["text"] = "<<EOT\nhello\nEOT",
            ["bb"] = "2",
        };

        string result = InputValues(values, compact: true);

        Assert.Equal(Lines("a    = 1", "text = <<EOT", "hello", "EOT", "bb   = 2"), result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_DescriptionComments_BreakAlignmentRuns()
    {
        var module = TerraformDotnet.Module.TerraformModule.LoadFromContent(
            System.Text.Encoding.UTF8.GetBytes("""
                variable "a" {
                  type        = string
                  description = "First."
                }

                variable "long_name" {
                  type        = string
                  description = "Second."
                }
                """));

        var call = new ModuleCallBuilder("svc", module).Source("./m").Set("a", "1").Set("long_name", "2").Build();
        var emitter = new ModuleCallEmitter(call);

        string result = emitter.EmitInputValues(
            new Dictionary<string, InputValue> { ["a"] = "1", ["long_name"] = "2" },
            compactSpacing: true);

        Assert.Equal(
            Lines("# (Required) First.", "a = 1", "# (Required) Second.", "long_name = 2"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitInputValues_NestedMultiLineValues_KeepIndentation()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["x"] = "[\n  {\n    outer = {\n      leaf = 1\n    }\n  }\n]",
            ["y"] = "2",
        };

        string result = InputValues(values);

        Assert.Equal(
            Lines("x = [", "  {", "    outer = {", "      leaf = 1", "    }", "  }", "]", "", "y = 2"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public void EmitInputValues_EmptyDictionary_ReturnsEmpty()
    {
        Assert.Equal("", InputValues(new Dictionary<string, InputValue>()));
    }

    [Fact]
    public async Task EmitInputValues_BracketsInsideStrings_DoNotCountAsMultiLine()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = "\"{ not open\"",
            ["bb"] = "\"[ also not\"",
        };

        string result = InputValues(values, compact: true);

        Assert.Equal(Lines("a  = \"{ not open\"", "bb = \"[ also not\""), result);
        await AssertFmtStableAsync(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmitInputValues_GeneratedCombinations_AreStableUnderTerraformFmt(bool compact)
    {
        string[] shapes =
        [
            "1",
            "\"text\"",
            "[\"a\", \"b\"]",
            "{ a = 1 }",
            "{\n  x = 1\n}",
            "[\n  {\n    a = 1\n  }\n]",
            "<<EOT\nbody\nEOT",
            "merge(\n  var.a,\n  {\n    b = 1\n  }\n)",
        ];

        string[] names = ["a", "bb", "long_name", "x", "another_long_name"];
        var random = new Random(compact ? 11 : 12);
        var values = new Dictionary<string, InputValue>();

        for (int i = 0; i < 150; i++)
        {
            string name = names[random.Next(names.Length)] + i;
            string shape = shapes[random.Next(shapes.Length)];
            string? comment = random.Next(0, 3) == 0 ? "c" + i : null;
            values[name] = new InputValue(shape, comment);
        }

        string result = InputValues(values, compact);

        Assert.Equal(result, await terraform.FormatAsync(result));
    }

    [Fact]
    public async Task EmitModuleBlock_GeneratedArguments_AreStableUnderTerraformFmt()
    {
        string[] shapes =
        [
            "1",
            "\"text\"",
            "{ a = 1 }",
            "{\n  x = 1\n}",
            "[\n  {\n    a = 1\n  }\n]",
            "<<EOT\nbody\nEOT",
            "merge(\n  var.a,\n  {\n    b = 1\n  }\n)",
        ];

        string[] names = ["a", "bb", "long_name", "x", "another_long_name"];
        var random = new Random(7);
        var builder = new ModuleCallBuilder("svc").Source("./modules/svc").Version("1.0.0");

        for (int i = 0; i < 100; i++)
        {
            builder.Set(names[random.Next(names.Length)] + i, shapes[random.Next(shapes.Length)]);
        }

        string result = new ModuleCallEmitter(builder.Build()).EmitModuleBlock();

        Assert.Equal(result + "\n", await terraform.FormatAsync(result + "\n"));
    }

    [Fact]
    public async Task EmitModuleBlock_ValueWithoutIndentation_IsReindented()
    {
        var emitter = EmitterFor(b => b.Set("rules", "[\n{\nname = \"a\"\nport = 80\n},\n]"));

        string result = emitter.EmitModuleBlock();

        Assert.Equal(
            Block(
                "module \"svc\" {",
                "  source = \"./modules/svc\"",
                "",
                "  rules = [",
                "    {",
                "      name = \"a\"",
                "      port = 80",
                "    },",
                "  ]",
                "}"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitModuleBlock_HeredocInsideObject_KeepsBodyVerbatim()
    {
        var emitter = EmitterFor(b => b.Set("config", "{\n  script = <<EOT\n#!/bin/sh\n  echo { hi\nEOT\n  other = 1\n}"));

        string result = emitter.EmitModuleBlock();

        Assert.Equal(
            Block(
                "module \"svc\" {",
                "  source = \"./modules/svc\"",
                "",
                "  config = {",
                "    script = <<EOT",
                "#!/bin/sh",
                "  echo { hi",
                "EOT",
                "    other  = 1",
                "  }",
                "}"),
            result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public void EmitModuleBlock_InvalidValue_IsWrittenAsGiven()
    {
        var emitter = EmitterFor(b => b.Set("broken", "{ a = "));

        string result = emitter.EmitModuleBlock();

        Assert.Equal(
            Block(
                "module \"svc\" {",
                "  source = \"./modules/svc\"",
                "",
                "  broken = { a = ",
                "}"),
            result);
    }

    [Fact]
    public async Task EmitInputValues_ValueWithoutIndentation_IsReindented()
    {
        var values = new Dictionary<string, InputValue>
        {
            ["a"] = new("{\nx = 1\nlong = [\n1,\n]\n}", "note"),
        };

        string result = InputValues(values);

        Assert.Equal(Lines("a = {", "  x = 1", "  long = [", "    1,", "  ]", "} # note"), result);
        await AssertFmtStableAsync(result);
    }

    [Fact]
    public async Task EmitVariableDeclarations_AllOptions_AreStableUnderTerraformFmt()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/compute-module");
        var builder = new ModuleCallBuilder("compute", module).Source("./modules/compute");

        foreach (var variable in module.Variables)
        {
            builder.Set(variable.Name, $"var.{variable.Name}");
        }

        var call = builder.Build();
        var options = new VariableDeclarationOptions
        {
            IncludeType = true,
            IncludeDescription = true,
            IncludeDefault = true,
        };

        string result = new ModuleCallEmitter(call).EmitVariableDeclarations(options);

        Assert.Equal(result, await terraform.FormatAsync(result));
    }

    // ── EmitExpression ──────────────────────────────────────────

    [Fact]
    public void EmitExpression_TupleOfObjects_ExpandsOneElementPerLine()
    {
        var tuple = new TerraformDotnet.Hcl.Nodes.HclTupleExpression();

        for (int i = 0; i < 2; i++)
        {
            var obj = new TerraformDotnet.Hcl.Nodes.HclObjectExpression();
            obj.Elements.Add(new TerraformDotnet.Hcl.Nodes.HclObjectElement
            {
                Key = new TerraformDotnet.Hcl.Nodes.HclVariableExpression { Name = "id" },
                Value = new TerraformDotnet.Hcl.Nodes.HclLiteralExpression
                {
                    Kind = TerraformDotnet.Hcl.Nodes.HclLiteralKind.Number,
                    Value = i.ToString(),
                },
            });
            tuple.Elements.Add(obj);
        }

        string result = ModuleCallEmitter.EmitExpression(tuple);

        Assert.Equal("[\n  {\n    id = 0\n  },\n  {\n    id = 1\n  }\n]", result);
    }

    [Fact]
    public void EmitExpression_EmptyTuple_IsInline()
    {
        Assert.Equal("[]", ModuleCallEmitter.EmitExpression(new TerraformDotnet.Hcl.Nodes.HclTupleExpression()));
    }
}
