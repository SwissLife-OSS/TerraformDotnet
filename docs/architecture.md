# TerraformDotnet.Hcl — Architecture

## Design Philosophy

The library follows three core principles:

1. **Zero-copy** — Token values are slices of the original UTF-8 input. The reader allocates only when the caller explicitly requests a decoded string via `GetString()`.
2. **Stack-first** — The reader is a `ref struct` that lives entirely on the stack. Depth tracking uses simple integer fields, not heap-allocated stacks.
3. **Pull-based** — The reader exposes a `Read()` → inspect → `Read()` loop. Callers control the pace and can bail out early without processing the entire input.

## Component Overview

```
┌──────────────────────────────────────────────────────────────┐
│                          HclFile.Load()                       │
│                     (convenience entry point)                 │
└──────────────┬──────────────────────────────┬────────────────┘
               │                              │
               ▼                              ▼
┌──────────────────────┐         ┌──────────────────────────┐
│   Utf8HclReader      │         │   HclFileParser          │
│   (ref struct)       │────────▶│   (recursive descent)    │
│                      │ tokens  │                          │
│   - Scanning         │         │   builds                 │
│   - Strings          │         │         │                │
│   - Parsing          │         │         ▼                │
└──────────────────────┘         │  ┌───────────────┐       │
                                 │  │  HclFile (AST)│       │
                                 │  └───────┬───────┘       │
                                 └──────────┼───────────────┘
                                            │
                    ┌───────────────────────┼───────────────────────┐
                    │                       │                       │
                    ▼                       ▼                       ▼
         ┌──────────────────┐   ┌───────────────────┐   ┌──────────────────┐
         │  HclFileEmitter  │   │  IHclVisitor      │   │  HclEvaluator    │
         │  + Utf8HclWriter │   │  (traversal)      │   │  (resolution)    │
         │                  │   │                   │   │                  │
         │  ──▶ UTF-8 bytes │   │  custom walks     │   │  ──▶ HclValue   │
         └──────────────────┘   └───────────────────┘   └──────────────────┘
```

## Layer Details

### Reader (`TerraformDotnet.Hcl.Reader`)

| Type | Role |
|------|------|
| `Utf8HclReader` | `ref struct` tokenizer on `ReadOnlySpan<byte>`. Pull-based `Read()` API. |
| `HclReaderOptions` | `readonly struct` — `MaxDepth` (default 64), `ReadComments` (default false). |
| `HclReaderState` | `readonly struct` — captures position for streaming resume. |

The reader is split across partial files for maintainability:

- **`Utf8HclReader.cs`** — fields, constructors, properties, value accessors
- **`Utf8HclReader.Scanning.cs`** — byte-level helpers, whitespace, comments, identifiers, numbers, operators
- **`Utf8HclReader.Strings.cs`** — quoted strings, heredocs, escape decoding, indented heredoc stripping
- **`Utf8HclReader.Parsing.cs`** — structural parsing (`Read()`, body elements, block labels, expression tokens)

**Token flow**: The reader emits tokens in a flat stream. For a block like:

```hcl
resource "aws_instance" "web" {
  ami = "value"
}
```

The token sequence is:

```
BlockType("resource") → BlockLabel("aws_instance") → BlockLabel("web") →
BlockStart → AttributeName("ami") → StringLiteral("value") → BlockEnd → Eof
```

**Expression context**: When `=` is encountered after an attribute name, the reader enters expression mode. It tracks nesting depth for `()`, `[]`, `{}` so that newlines inside nested expressions are ignored. A newline at zero nesting depth ends the attribute value and transitions back to body mode.

### Nodes (`TerraformDotnet.Hcl.Nodes`)

The AST is a simple object model:

| Node | Description |
|------|-------------|
| `HclFile` | Root — contains `HclBody` and `DanglingComments` |
| `HclBody` | List of `HclAttribute` and `HclBlock` |
| `HclBlock` | `Type`, `Labels`, nested `Body` |
| `HclAttribute` | `Name` and `Value` (an `HclExpression`) |

All nodes inherit from `HclNode`, which provides:
- `Mark Start` / `Mark End` — source positions
- `List<HclComment> LeadingComments` — comments above the node
- `HclComment? TrailingComment` — inline comment on the same line
- `Accept(IHclVisitor)` — visitor pattern
- `DeepClone()` — deep copy

**Expression nodes** (all extend `HclExpression`):

| Node | Example |
|------|---------|
| `HclLiteralExpression` | `42`, `true`, `"hello"`, `null` |
| `HclVariableExpression` | `var`, `local` |
| `HclAttributeAccessExpression` | `var.name` |
| `HclIndexExpression` | `list[0]` |
| `HclBinaryExpression` | `a + b`, `x == y` |
| `HclUnaryExpression` | `-x`, `!enabled` |
| `HclConditionalExpression` | `cond ? a : b` |
| `HclFunctionCallExpression` | `length(list)` |
| `HclTupleExpression` | `[1, 2, 3]` |
| `HclObjectExpression` | `{ key = "value" }` |
| `HclForExpression` | `[for v in list : v]` |
| `HclSplatExpression` | `list.*.name`, `list[*].id` |
| `HclTemplateExpression` | `"hello ${name}"` (decomposed parts) |
| `HclTemplateWrapExpression` | `"${var.value}"` (unwrap-eligible) |

**Comment model**:

| Type | Description |
|------|-------------|
| `HclComment` | `Text`, `Style`, `Start`, `End` |
| `HclCommentStyle` | `Line` (`//`), `Hash` (`#`), `Block` (`/* */`) |

Comments attach to the nearest subsequent node as `LeadingComments`. Inline comments (same line as a node) are stored as `TrailingComment`. Comments between blocks or at file end that don't attach to any node go into `HclFile.DanglingComments`.

### Writer (`TerraformDotnet.Hcl.Writer`)

| Type | Role |
|------|------|
| `Utf8HclWriter` | `sealed class : IDisposable`. Writes to `IBufferWriter<byte>` or `Stream`. |
| `HclWriterOptions` | `NewLineStyle` (default LF), `PreserveComments` (default true). |
| `HclFileEmitter` | Walks `HclFile` AST and writes via `Utf8HclWriter`. |

**Canonical formatting rules** (matching `terraform fmt`):
- 2-space indentation per nesting level
- `=` signs aligned within the same block body (longest attribute name sets the column)
- Single blank line between top-level blocks
- Opening `{` on the same line as block/object header
- Closing `}` on its own line at the parent's indent level
- No trailing whitespace
- LF line endings

The writer has two usage modes:

1. **Imperative** — call `WriteBlockStart()`, `WriteAttribute()`, etc. directly
2. **AST-driven** — pass an `HclFile` to `HclFileEmitter.Emit()`

### Evaluation (`TerraformDotnet.Hcl.Evaluation`)

| Type | Role |
|------|------|
| `HclEvaluator` | Walks `HclExpression` AST, resolves to `HclValue`. |
| `HclEvaluationContext` | Variable bindings with nested scope support. |
| `HclValue` | Tagged union: String, Number, Bool, Null, Tuple, Object, Unknown. |
| `HclValueType` | Enum identifying the value type. |
| `HclUnresolvableException` | Thrown when a variable is not found. |

**What gets resolved**:
- Literal values → direct `HclValue`
- Variable references → lookup in context
- Attribute access / index access → traverse resolved values
- Binary and unary operations → computed result
- Conditionals → evaluate predicate, return appropriate branch
- Tuple/object constructors → build collections
- For expressions → iterate and collect
- Template interpolation → resolve and concatenate

**What does NOT get resolved**:
- Function calls → return `HclValue.Unknown(functionName, args)`
- Any expression depending on an unknown → propagates unknown

**Key use-case**: Extracting default values from Terraform variable blocks:

```csharp
var file = HclFile.Load(hcl);
var variable = file.Body.Blocks.First(b => b.Type == "variable");
var defaultAttr = variable.Body.Attributes.FirstOrDefault(a => a.Name == "default");

if (defaultAttr is not null)
{
    var evaluator = new HclEvaluator();
    HclValue defaultValue = evaluator.Evaluate(defaultAttr.Value, new HclEvaluationContext());
}
```

## Performance Characteristics

| Aspect | Approach |
|--------|----------|
| Reader allocation | Zero for tokenization; string decode allocates only when `GetString()` is called |
| Token values | Slices of input `ReadOnlySpan<byte>` — no copies |
| Nesting depth | Integer counter; checked against `MaxDepth` to prevent stack overflow |
| Writer output | Writes directly to `IBufferWriter<byte>` — no intermediate string building |
| AST memory | One object per node; comments stored as attached lists |
| AOT safety | `IsAotCompatible = true`, no reflection, no dynamic code generation |

## Error Handling

All errors include source position information (`Mark` with line, column, offset):

| Exception | When |
|-----------|------|
| `HclSyntaxException` | Malformed input (unterminated strings, unexpected characters, etc.) |
| `HclSemanticException` | Valid syntax but invalid semantics (duplicate attributes in same body) |
| `MaxRecursionDepthExceededException` | Block nesting exceeds `MaxDepth` |
| `HclUnresolvableException` | Variable not found during evaluation |

All inherit from `HclException`, which itself extends `Exception`.
