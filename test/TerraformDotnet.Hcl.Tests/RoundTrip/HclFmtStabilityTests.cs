using System.Buffers;
using System.Text;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Tests.Common;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.RoundTrip;

/// <summary>
/// Verifies that emitted HCL is already in <c>terraform fmt</c> canonical form. Every case is
/// parsed and re-emitted and must equal the canonical text, which the real <c>terraform fmt</c>
/// must also leave unchanged.
/// </summary>
public sealed class HclFmtStabilityTests
{
    public static TheoryData<string, string> CanonicalCases => new()
    {
        { "multi-line object", Lines("locals = {", "  a = 1", "}") },
        { "aligned single-line attributes", Lines("a   = 1", "bcd = 2") },
        {
            "multi-line attribute splits alignment runs",
            Lines(
                "a = 1",
                "long_name = {",
                "  x = 1",
                "}",
                "bb  = 2",
                "ccc = 3")
        },
        {
            "list of objects expands one element per line",
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
                "]")
        },
        {
            "single element list with an object",
            Lines("ports = [", "  {", "    name = \"http\"", "  }", "]")
        },
        { "empty tuple and object stay inline", Lines("a = []", "b = {}") },
        { "single-line tuple stays inline", Lines("names = [\"a\", \"b\", \"c\"]") },
        {
            "tuple of tuples with multi-line content",
            Lines(
                "matrix = [",
                "  [",
                "    {",
                "      a = 1",
                "    }",
                "  ],",
                "  [",
                "    {",
                "      a = 2",
                "    }",
                "  ]",
                "]")
        },
        {
            "mixed single-line and multi-line elements",
            Lines("items = [", "  1,", "  {", "    a = 1", "  },", "  \"x\"", "]")
        },
        {
            "object pads only adjacent single-line values",
            Lines(
                "m = {",
                "  a   = 1",
                "  bbb = 2",
                "  nested = {",
                "    x = 1",
                "  }",
                "  c  = 3",
                "  dd = 4",
                "}")
        },
        {
            "object nested in object nested in tuple",
            Lines(
                "x = [",
                "  {",
                "    outer = {",
                "      inner = {",
                "        leaf = 1",
                "      }",
                "    }",
                "  }",
                "]")
        },
        {
            "function call with multi-line arguments",
            Lines(
                "x = merge(",
                "  {",
                "    a = 1",
                "  },",
                "  {",
                "    b = 2",
                "  }",
                ")")
        },
        {
            "function call with expanded final argument",
            Lines(
                "x = concat([",
                "  {",
                "    a = 1",
                "  }",
                "], var.more...)")
        },
        {
            "function call hugs a single multi-line final argument",
            Lines("x = merge(var.a, {", "  b = 1", "})")
        },
        {
            "function call hugs a single multi-line first argument",
            Lines("x = merge({", "  b = 1", "}, var.a)")
        },
        {
            "nested function calls hug",
            Lines("x = jsonencode(merge(var.a, {", "  b = [", "    {", "      c = 1", "    }", "  ]", "}))")
        },
        {
            "function call with two multi-line arguments expands",
            Lines("x = merge(", "  {", "    a = 1", "  },", "  {", "    b = 2", "  }", ")")
        },
        {
            "function call hugs a multi-line conditional",
            Lines("x = merge(var.a, var.on ? {", "  b = 1", "  } : {", "  c = 2", "})")
        },
        { "function call with single-line arguments", Lines("x = merge(var.a, var.b)") },
        {
            "for expression with object body",
            Lines("x = [for k in var.items : {", "  name = k", "}]")
        },
        { "blank line splits alignment runs", Lines("a = 1", "", "longer = 2") },
        { "leading comment splits alignment runs", Lines("a = 1", "# comment", "longer = 2") },
        {
            "block with attributes and nested block",
            Lines(
                "resource \"x\" \"y\" {",
                "  a  = 1",
                "  bb = 2",
                "  nested {",
                "    c = [",
                "      {",
                "        d = 1",
                "      }",
                "    ]",
                "  }",
                "}")
        },
        {
            "quoted and bare keys",
            Lines("m = {", "  \"quoted-key\" = 1", "  bare         = 2", "}")
        },
        { "object for expression", Lines("x = { for k, v in var.m : k => v }") },
        { "object for expression with grouping", Lines("x = { for k, v in var.m : k => v... }") },
        { "object for expression with condition", Lines("x = { for k, v in var.m : k => v if v != null }") },
        { "colon style object keys", Lines("x = {", "  \"a\" : 1", "  \"bb\" : 2", "}") },
        {
            "conditional with multi-line branches",
            Lines(
                "x = var.c ? {",
                "  a = 1",
                "  } : {",
                "  b = 2",
                "}")
        },
        {
            "conditional with single-line branches",
            Lines("x = var.c ? [1] : []")
        },
        {
            "colon entries do not align with equals entries",
            Lines("x = {", "  a   = 1", "  bbb = 2", "  \"c\" : 3", "  dd = 4", "}")
        },
        {
            "conditional with multi-line true branch only",
            Lines("x = var.c ? {", "  a = 1", "} : null")
        },
        {
            "conditional with multi-line false branch only",
            Lines("x = var.c ? null : {", "  a = 1", "}")
        },
        {
            "conditional with multi-line tuple branches",
            Lines(
                "x = var.c ? [",
                "  {",
                "    a = 1",
                "  }",
                "  ] : [",
                "  {",
                "    b = 2",
                "  }",
                "]")
        },
        {
            "object for expression with multi-line value",
            Lines("x = { for k, v in var.m : k => {", "  name = v", "} }")
        },
        {
            "list comprehension of tuples",
            Lines("x = [for i in var.l : [i, i]]")
        },
        {
            "object in function call argument list",
            Lines("x = toset([", "  {", "    a = 1", "  }", "])")
        },
        {
            "many attributes keep source order",
            string.Concat(Enumerable.Range(0, 40).Select(i => $"a{i:D2} = {i}\n"))
        },
    };

    [Theory]
    [MemberData(nameof(CanonicalCases))]
    public void Emit_CanonicalInput_IsIdempotent(string name, string canonical)
    {
        Assert.NotNull(name);
        Assert.Equal(canonical, EmitFile(Parse(canonical)));
    }

    [Fact]
    public void Emit_HeredocAttribute_IsAlignedWithNeighbours()
    {
        var file = new HclFile();
        file.Body.Attributes.Add(new HclAttribute { Name = "a", Value = Num("1") });
        file.Body.Attributes.Add(new HclAttribute { Name = "bcd", Value = Heredoc("hello\n") });
        file.Body.Attributes.Add(new HclAttribute { Name = "e", Value = Num("2") });

        Assert.Equal("a   = 1\nbcd = <<EOT\nhello\nEOT\ne   = 2\n", EmitFile(file));
    }

    private static HclTemplateExpression Heredoc(string content)
        => new() { RawContent = content, IsHeredoc = true, HeredocMarker = "EOT" };

    private static HclLiteralExpression Num(string value)
        => new() { Kind = HclLiteralKind.Number, Value = value };

    [Fact]
    public void Emit_GeneratedNestedExpressions_RoundTripThroughParser()
    {
        var random = new Random(20240607);

        for (int i = 0; i < 2000; i++)
        {
            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "value", Value = Generate(random, depth: 0) });

            string emitted = EmitFile(file);
            string reEmitted;

            try
            {
                reEmitted = EmitFile(Parse(emitted));
            }
            catch (HclException ex)
            {
                Assert.Fail($"Emitted text does not parse: {ex.Message}\n{emitted}");
                return;
            }

            Assert.Equal(emitted, reEmitted);
        }
    }

    private static readonly HclBinaryOperator[] BinaryOperators = Enum.GetValues<HclBinaryOperator>();

    private static readonly string[] EscapedStrings =
    [
        "quote \" inside",
        "back\\slash",
        "new\nline and\ttab",
        "unicode \u00e9 \u4e2d",
        "dollar $ and percent % alone",
        "cidrs: ${join(\", \", var.cidrs)} $${literal}",
        "smile \U0001F600",
        "",
    ];

    private static HclExpression Generate(Random random, int depth)
    {
        int kind = depth >= 3 ? random.Next(0, 4) : random.Next(0, 15);

        switch (kind)
        {
            case 0:
                return Num(random.Next(0, 100).ToString());
            case 1:
                return Str("v" + random.Next(0, 1000));
            case 2:
                return new HclVariableExpression { Name = "var.x" + random.Next(0, 5) };
            case 3:
                return Str(EscapedStrings[random.Next(EscapedStrings.Length)]);
            case 4:
            case 5:
            {
                var tuple = new HclTupleExpression();
                int count = random.Next(0, 4);
                for (int i = 0; i < count; i++)
                {
                    tuple.Elements.Add(WithComments(random, Generate(random, depth + 1)));
                }

                AddDanglingComment(random, tuple.DanglingComments);

                return tuple;
            }

            case 6:
            case 7:
            {
                var obj = new HclObjectExpression();
                int count = random.Next(0, 5);
                for (int i = 0; i < count; i++)
                {
                    var element = new HclObjectElement
                    {
                        Key = GenerateKey(random, i),
                        Value = Generate(random, depth + 1),
                        TrailingComment = MaybeComment(random),
                    };
                    element.ForceKey = element.Key is HclVariableExpression { Name: ['v', 'a', 'r', ..] };
                    if (MaybeComment(random) is { } leading)
                    {
                        element.LeadingComments.Add(leading);
                    }

                    obj.Elements.Add(element);
                }

                AddDanglingComment(random, obj.DanglingComments);

                return obj;
            }

            case 8:
            {
                var call = new HclFunctionCallExpression { Name = "fn" };
                int count = random.Next(1, 4);
                for (int i = 0; i < count; i++)
                {
                    call.Arguments.Add(WithComments(random, Generate(random, depth + 1)));
                }

                call.ExpandFinalArgument = random.Next(0, 4) == 0;
                AddDanglingComment(random, call.DanglingComments);

                return call;
            }

            case 9:
                return new HclConditionalExpression
                {
                    Condition = random.Next(0, 2) == 0 ? new HclVariableExpression { Name = "var.c" } : Comparison(random),
                    TrueResult = Generate(random, depth + 1),
                    FalseResult = Generate(random, depth + 1),
                };
            case 10:
            case 11:
                return new HclBinaryExpression
                {
                    Left = Generate(random, depth + 1),
                    Operator = BinaryOperators[random.Next(BinaryOperators.Length)],
                    Right = Generate(random, depth + 1),
                };
            case 12:
                return new HclUnaryExpression
                {
                    Operator = random.Next(0, 2) == 0 ? HclUnaryOperator.Negate : HclUnaryOperator.Not,
                    Operand = Generate(random, depth + 1),
                };
            default:
            {
                bool isObject = kind == 14;
                bool hasValueVariable = random.Next(0, 2) == 0;

                return new HclForExpression
                {
                    KeyVariable = hasValueVariable ? "k" : "x",
                    ValueVariable = hasValueVariable ? "v" : null,
                    Collection = random.Next(0, 2) == 0 ? new HclVariableExpression { Name = "var.list" } : Generate(random, depth + 1),
                    KeyExpression = isObject ? new HclVariableExpression { Name = hasValueVariable ? "k" : "x" } : null,
                    ValueExpression = Generate(random, depth + 1),
                    Condition = random.Next(0, 3) == 0 ? Comparison(random) : null,
                    IsObjectFor = isObject,
                    IsGrouped = isObject && random.Next(0, 3) == 0,
                };
            }
        }
    }

    private static HclExpression GenerateKey(Random random, int index) => random.Next(0, 4) switch
    {
        0 => Str($"key {index}"),
        1 => new HclVariableExpression { Name = $"var.key{index}" },
        _ => Ident(new string('k', random.Next(1, 8)) + index),
    };

    private static HclBinaryExpression Comparison(Random random) => new()
    {
        Left = new HclVariableExpression { Name = "var.n" },
        Operator = HclBinaryOperator.GreaterThan,
        Right = Num(random.Next(0, 10).ToString()),
    };

    private static HclExpression WithComments(Random random, HclExpression expression)
    {
        if (MaybeComment(random) is { } leading)
        {
            expression.LeadingComments.Add(leading);
        }

        expression.TrailingComment = MaybeComment(random);

        return expression;
    }

    private static void AddDanglingComment(Random random, List<HclComment> comments)
    {
        if (MaybeComment(random) is { } comment)
        {
            comments.Add(comment);
        }
    }

    private static HclComment? MaybeComment(Random random) => random.Next(0, 8) switch
    {
        0 => new HclComment { Text = " hash", Style = HclCommentStyle.Hash },
        1 => new HclComment { Text = " line", Style = HclCommentStyle.Line },
        2 => new HclComment { Text = " block ", Style = HclCommentStyle.Block },
        _ => null,
    };

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static HclVariableExpression Ident(string value) => new() { Name = value };

    private static HclLiteralExpression Str(string value)
        => new() { Kind = HclLiteralKind.String, Value = value };

    private static string EmitFile(HclFile file)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        new HclFileEmitter(writer).Emit(file);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static HclFile Parse(string hcl) => HclFileParser.Parse(Encoding.UTF8.GetBytes(hcl));

    /// <summary>Checks against the real <c>terraform fmt</c> running in a container.</summary>
    [Collection(TerraformFmtCollection.Name)]
    public sealed class AgainstTerraformFmt(TerraformFmtResource terraform)
    {
        [Theory]
        [MemberData(nameof(CanonicalCases), MemberType = typeof(HclFmtStabilityTests))]
        public async Task Emit_CanonicalInput_IsStableUnderTerraformFmt(string name, string canonical)
        {
            Assert.NotNull(name);
            string emitted = EmitFile(Parse(canonical));
            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
        }

        [Fact]
        public async Task Emit_ProgrammaticMultiLineTuple_IsStableUnderTerraformFmt()
        {
            var tuple = new HclTupleExpression();

            for (int i = 0; i < 3; i++)
            {
                var obj = new HclObjectExpression();
                obj.Elements.Add(new HclObjectElement { Key = Ident("name"), Value = Str($"n{i}") });
                obj.Elements.Add(new HclObjectElement { Key = Ident("port_number"), Value = Str($"{i}") });
                tuple.Elements.Add(obj);
            }

            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "ports", Value = tuple });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
            Assert.StartsWith("ports = [\n  {\n    name        = \"n0\"\n    port_number = \"0\"\n  },\n", emitted);
        }

        [Fact]
        public async Task Emit_HeredocAttribute_IsStableUnderTerraformFmt()
        {
            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "a", Value = Num("1") });
            file.Body.Attributes.Add(new HclAttribute { Name = "bcd", Value = Heredoc("hello\n") });
            file.Body.Attributes.Add(new HclAttribute { Name = "e", Value = Num("2") });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
        }

        [Fact]
        public async Task Emit_HeredocInsideTuple_IsStableUnderTerraformFmt()
        {
            var tuple = new HclTupleExpression();
            tuple.Elements.Add(Heredoc("one\n"));
            tuple.Elements.Add(Str("two"));
            tuple.Elements.Add(Heredoc("three\n"));

            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "x", Value = tuple });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
            Assert.DoesNotContain("EOT, ", emitted);
            Assert.DoesNotContain("EOT,", emitted);
        }

        [Fact]
        public async Task Emit_HeredocInsideFunctionCall_IsStableUnderTerraformFmt()
        {
            var call = new HclFunctionCallExpression { Name = "format" };
            call.Arguments.Add(Heredoc("%s\n"));
            call.Arguments.Add(Str("x"));

            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "x", Value = call });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
            Assert.Equal("x = format(\n  <<EOT\n%s\nEOT\n  ,\n  \"x\"\n)\n", emitted);
        }

        [Fact]
        public async Task Emit_FunctionCallWithMultiLineArgumentAndHeredoc_Expands()
        {
            var obj = new HclObjectExpression();
            obj.Elements.Add(new HclObjectElement { Key = Ident("a"), Value = Num("1") });

            var call = new HclFunctionCallExpression { Name = "templatefile" };
            call.Arguments.Add(Heredoc("${a}\n"));
            call.Arguments.Add(obj);

            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "x", Value = call });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
            Assert.Equal("x = templatefile(\n  <<EOT\n${a}\nEOT\n  ,\n  {\n    a = 1\n  }\n)\n", emitted);
        }

        [Fact]
        public async Task Emit_HuggedCallInsideTupleAndObject_IsStableUnderTerraformFmt()
        {
            var inner = new HclObjectExpression();
            inner.Elements.Add(new HclObjectElement { Key = Ident("b"), Value = Num("1") });

            var call = new HclFunctionCallExpression { Name = "merge" };
            call.Arguments.Add(new HclVariableExpression { Name = "var.a" });
            call.Arguments.Add(inner);

            var obj = new HclObjectExpression();
            obj.Elements.Add(new HclObjectElement { Key = Ident("long_key"), Value = Num("1") });
            obj.Elements.Add(new HclObjectElement { Key = Ident("k"), Value = call });
            obj.Elements.Add(new HclObjectElement { Key = Ident("z"), Value = Num("2") });

            var tuple = new HclTupleExpression();
            tuple.Elements.Add(obj);

            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "x", Value = tuple });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
            Assert.Equal(
                Lines(
                    "x = [",
                    "  {",
                    "    long_key = 1",
                    "    k = merge(var.a, {",
                    "      b = 1",
                    "    })",
                    "    z = 2",
                    "  }",
                    "]"),
                emitted);
        }

        [Fact]
        public async Task Emit_HeredocAsObjectValue_IsStableUnderTerraformFmt()
        {
            var obj = new HclObjectExpression();
            obj.Elements.Add(new HclObjectElement { Key = Ident("a"), Value = Num("1") });
            obj.Elements.Add(new HclObjectElement { Key = Ident("script"), Value = Heredoc("echo\n") });
            obj.Elements.Add(new HclObjectElement { Key = Ident("b"), Value = Num("2") });

            var file = new HclFile();
            file.Body.Attributes.Add(new HclAttribute { Name = "x", Value = obj });

            string emitted = EmitFile(file);

            Assert.Equal(emitted, await terraform.FormatAsync(emitted));
        }

        [Fact]
        public async Task Emit_GeneratedNestedExpressions_AreStableUnderTerraformFmt()
        {
            var random = new Random(20240607);
            var file = new HclFile();

            for (int i = 0; i < 300; i++)
            {
                file.Body.Attributes.Add(new HclAttribute { Name = $"attr_{i}", Value = Generate(random, depth: 0) });
            }

            string emitted = EmitFile(file);
            string formatted = await terraform.FormatAsync(emitted);

            // Guards against a generator that no longer reaches the interesting layouts.
            Assert.Contains("\n  } : {\n", emitted);
            Assert.Contains("...\n", emitted);
            Assert.Contains("(\n  {\n", emitted);

            if (emitted != formatted)
            {
                string[] a = emitted.Split('\n');
                string[] b = formatted.Split('\n');
                int line = Enumerable.Range(0, Math.Min(a.Length, b.Length)).First(n => a[n] != b[n]);
                string context = string.Join("\n", a.Skip(Math.Max(0, line - 12)).Take(24));
                Assert.Fail($"Not fmt-stable at line {line + 1}: emitted '{a[line]}' but fmt wants '{b[line]}'.\n{context}");
            }
        }
    }
}
