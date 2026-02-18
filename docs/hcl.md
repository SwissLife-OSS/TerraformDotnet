# TerraformDotnet.Hcl — Usage Guide

Complete guide for the HCL parser, writer, and evaluation engine.

## Table of Contents

- [Installation](#installation)
- [Parsing HCL](#parsing-hcl)
  - [Parse from UTF-8 bytes](#parse-from-utf-8-bytes)
  - [Parse from a file](#parse-from-a-file)
  - [Navigate the AST](#navigate-the-ast)
  - [Expression types](#expression-types)
  - [Comments](#comments)
- [Low-Level Reader](#low-level-reader)
  - [Token-by-token scanning](#token-by-token-scanning)
  - [Reader options](#reader-options)
- [Writing HCL](#writing-hcl)
  - [Imperative writer](#imperative-writer)
  - [Attribute alignment](#attribute-alignment)
  - [Blocks and nesting](#blocks-and-nesting)
  - [Collections](#collections)
  - [Comments](#writing-comments)
  - [AST-driven emission](#ast-driven-emission)
- [Round-Trip Editing](#round-trip-editing)
- [Expression Evaluation](#expression-evaluation)
  - [Variable binding](#variable-binding)
  - [Supported operations](#supported-operations)
  - [Unknown values](#unknown-values)
- [Error Handling](#error-handling)

---

## Installation

```shell
dotnet add package TerraformDotnet.Hcl
```

Requires .NET 10+. AOT-compatible — no reflection, trimmer-safe.

---

## Parsing HCL

### Parse from UTF-8 bytes

The parser operates on `ReadOnlySpan<byte>` for zero-copy performance. Use the `u8` suffix for literals:

```csharp
using TerraformDotnet.Hcl.Nodes;

var hcl = """
    resource "aws_instance" "web" {
      ami           = "ami-0c55b159cbfafe1f0"
      instance_type = "t2.micro"

      tags = {
        Name = "HelloWorld"
      }
    }
    """u8;

HclFile file = HclFile.Load(hcl);
```

### Parse from a file

```csharp
byte[] bytes = File.ReadAllBytes("main.tf");
HclFile file = HclFile.Load(bytes);
```

### Navigate the AST

The AST is a simple tree: `HclFile` → `HclBody` → `HclAttribute` / `HclBlock`.

```csharp
HclFile file = HclFile.Load(hcl);

// Top-level attributes
foreach (var attr in file.Body.Attributes)
{
    Console.WriteLine($"{attr.Name} = ...");
}

// Top-level blocks
foreach (var block in file.Body.Blocks)
{
    Console.WriteLine($"{block.Type} {string.Join(" ", block.Labels)}");

    // Nested attributes
    foreach (var attr in block.Body.Attributes)
    {
        Console.WriteLine($"  {attr.Name}");
    }

    // Nested blocks
    foreach (var inner in block.Body.Blocks)
    {
        Console.WriteLine($"  {inner.Type} {{ ... }}");
    }
}
```

### Expression types

Attribute values are `HclExpression` subclasses. Common patterns:

```csharp
HclAttribute attr = block.Body.Attributes[0];

switch (attr.Value)
{
    case HclLiteralExpression literal:
        // String: literal.Value = "hello"
        // Number: literal.Value = "42"
        // Bool:   literal.Value = "true"
        Console.WriteLine($"Literal: {literal.Value}");
        break;

    case HclVariableExpression variable:
        // Bare identifier: var, local, each
        Console.WriteLine($"Variable: {variable.Name}");
        break;

    case HclAttributeAccessExpression access:
        // Dot access: var.name, module.output
        Console.WriteLine($"{access.Source}.{access.Name}");
        break;

    case HclFunctionCallExpression call:
        // Function: length(list), lookup(map, key)
        Console.WriteLine($"{call.Name}({call.Arguments.Count} args)");
        break;

    case HclTupleExpression tuple:
        // List/tuple: [1, 2, 3]
        Console.WriteLine($"Tuple with {tuple.Elements.Count} elements");
        break;

    case HclObjectExpression obj:
        // Object: { key = value }
        Console.WriteLine($"Object with {obj.Elements.Count} elements");
        break;

    case HclTemplateWrapExpression wrap:
        // String interpolation: "${var.name}"
        Console.WriteLine($"Template wrap");
        break;

    case HclBinaryExpression binary:
        // Binary op: a + b, x == y
        Console.WriteLine($"{binary.Operator}");
        break;

    case HclConditionalExpression cond:
        // Ternary: condition ? true_val : false_val
        Console.WriteLine("Conditional");
        break;

    case HclForExpression forExpr:
        // For: [for v in list : v]
        Console.WriteLine($"For over {forExpr.CollectionExpression}");
        break;

    case HclSplatExpression splat:
        // Splat: list.*.name, list[*].id
        Console.WriteLine("Splat");
        break;
}
```

### Comments

Comments are attached to AST nodes. Leading comments are above the node, trailing comments are on the same line:

```csharp
HclFile file = HclFile.Load("""
    # This is a leading comment
    name = "value" # trailing comment
    """u8);

var attr = file.Body.Attributes[0];

foreach (var comment in attr.LeadingComments)
{
    Console.WriteLine($"Leading: {comment.Text}");
}

if (attr.TrailingComment is not null)
{
    Console.WriteLine($"Trailing: {attr.TrailingComment.Text}");
}

// Comments at the end of the file (not attached to any node)
foreach (var comment in file.DanglingComments)
{
    Console.WriteLine($"Dangling: {comment.Text}");
}
```

---

## Low-Level Reader

### Token-by-token scanning

For streaming or custom parsing, use `Utf8HclReader` directly:

```csharp
using TerraformDotnet.Hcl.Reader;

var hcl = """
    name  = "hello"
    count = 42
    """u8;

var reader = new Utf8HclReader(hcl);

while (reader.Read())
{
    Console.WriteLine($"{reader.TokenType}: {reader.GetString()}");
}
// AttributeName: name
// StringLiteral: hello
// AttributeName: count
// NumberLiteral: 42
// Eof:
```

The reader is a `ref struct` — zero allocations during tokenization. String values are decoded from UTF-8 only when you call `GetString()`.

### Reader options

```csharp
using TerraformDotnet.Hcl.Reader;

var options = new HclReaderOptions
{
    MaxDepth = 128,        // Max block nesting (default: 64)
    ReadComments = true,   // Emit comment tokens (default: false)
};

var reader = new Utf8HclReader(hcl, options);
```

---

## Writing HCL

### Imperative writer

`Utf8HclWriter` writes directly to `IBufferWriter<byte>` or `Stream`:

```csharp
using TerraformDotnet.Hcl.Writer;
using System.Buffers;
using System.Text;

var buffer = new ArrayBufferWriter<byte>();
using var writer = new Utf8HclWriter(buffer);

writer.WriteBlockStart("resource", "aws_s3_bucket", "assets");
writer.WriteAttribute("bucket", "my-assets-bucket");
writer.WriteBooleanAttribute("force_destroy", false);
writer.WriteNumberAttribute("max_size", 1024);
writer.WriteBlockEnd();
writer.Flush();

string hcl = Encoding.UTF8.GetString(buffer.WrittenSpan);
```

Output:

```hcl
resource "aws_s3_bucket" "assets" {
  bucket        = "my-assets-bucket"
  force_destroy = false
  max_size      = 1024
}
```

### Attribute alignment

Attributes in the same block are aligned to the longest name, matching `terraform fmt`. You can compute padding explicitly:

```csharp
var names = new List<string> { "source", "version", "department", "environment" };
Dictionary<string, int> padding = Utf8HclWriter.AlignAttributes(names);

// padding["source"]      → spaces to add after "source"
// padding["environment"] → 0 (longest name)
```

### Blocks and nesting

```csharp
writer.WriteBlockStart("terraform");

writer.WriteBlockStart("required_providers");
writer.WriteBlockStart("aws");
writer.WriteAttribute("source", "hashicorp/aws");
writer.WriteAttribute("version", "~> 5.0");
writer.WriteBlockEnd(); // aws
writer.WriteBlockEnd(); // required_providers

writer.WriteBlockEnd(); // terraform
```

### Collections

```csharp
// Tuple: ["a", "b", "c"]
writer.WriteAttributeName("allowed_actions", 0);
writer.WriteTupleStart();
writer.WriteStringValue("read");
writer.WriteStringValue("write");
writer.WriteTupleEnd();
writer.WriteAttributeEnd();

// Object: { key = "value" }
writer.WriteAttributeName("tags", 0);
writer.WriteObjectStart();
writer.WriteAttributeName("Name", 0);
writer.WriteStringValue("MyResource");
writer.WriteAttributeEnd();
writer.WriteObjectEnd();
writer.WriteAttributeEnd();
```

### Writing comments

```csharp
writer.WriteLineComment(" This is a comment");
// Output: # This is a comment
```

### AST-driven emission

Walk an `HclFile` AST to produce formatted output:

```csharp
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Writer;

HclFile file = HclFile.Load(hcl);

var buffer = new ArrayBufferWriter<byte>();
using var writer = new Utf8HclWriter(buffer);
var emitter = new HclFileEmitter(writer, preserveComments: true);
emitter.Emit(file);
writer.Flush();

string output = Encoding.UTF8.GetString(buffer.WrittenSpan);
```

The `HclFileEmitter` preserves comments by default and produces output identical to `terraform fmt`.

---

## Round-Trip Editing

Parse HCL, modify the AST, and emit back:

```csharp
// Parse
var hcl = """
    variable "region" {
      type    = string
      default = "us-east-1"
    }
    """u8;

HclFile file = HclFile.Load(hcl);

// Modify — change the default value
var variable = file.Body.Blocks[0];
var defaultAttr = variable.Body.Attributes.First(a => a.Name == "default");
defaultAttr.Value = new HclLiteralExpression("eu-west-1");

// Emit
var buffer = new ArrayBufferWriter<byte>();
using var writer = new Utf8HclWriter(buffer);
new HclFileEmitter(writer).Emit(file);
writer.Flush();
// variable "region" {
//   type    = string
//   default = "eu-west-1"
// }
```

Deep clone an AST for non-destructive modifications:

```csharp
HclFile original = HclFile.Load(hcl);
HclFile copy = (HclFile)original.DeepClone();

// Modify copy without affecting original
copy.Body.Blocks[0].Labels[0] = "new_name";
```

---

## Expression Evaluation

### Variable binding

Resolve expressions with known variable values:

```csharp
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

var hcl = """
    output "greeting" {
      value = "Hello, ${username}! You have ${count} items."
    }
    """u8;

HclFile file = HclFile.Load(hcl);
var valueAttr = file.Body.Blocks[0].Body.Attributes
    .First(a => a.Name == "value");

var ctx = new HclEvaluationContext();
ctx.SetVariable("username", HclValue.FromString("Alice"));
ctx.SetVariable("count", HclValue.FromNumber(42));

var evaluator = new HclEvaluator();
HclValue result = evaluator.Evaluate(valueAttr.Value, ctx);
Console.WriteLine(result.StringValue); // "Hello, Alice! You have 42 items."
```

### Supported operations

| Category | Operations |
|----------|-----------|
| **Literals** | Strings, numbers, booleans, null |
| **Arithmetic** | `+`, `-`, `*`, `/`, `%` |
| **Comparison** | `==`, `!=`, `<`, `>`, `<=`, `>=` |
| **Logic** | `&&`, `\|\|`, `!` |
| **Conditionals** | `condition ? true_val : false_val` |
| **String templates** | `"hello ${name}"` |
| **Collections** | Tuple `[a, b]` and object `{ k = v }` construction |
| **Access** | Attribute `.name`, index `[0]` |
| **For expressions** | `[for v in list : v]`, `{for k, v in map : k => v}` |

### Unknown values

Function calls and unresolvable references return `HclValue.Unknown` instead of throwing:

```csharp
var ctx = new HclEvaluationContext();
var evaluator = new HclEvaluator();

var hcl = "result = length(var.list)"u8;
HclFile file = HclFile.Load(hcl);
HclValue result = evaluator.Evaluate(file.Body.Attributes[0].Value, ctx);

if (result.Type == HclValueType.Unknown)
{
    Console.WriteLine("Value could not be fully resolved");
}
```

---

## Error Handling

All errors include source position information (`Mark` with line, column, byte offset):

```csharp
using TerraformDotnet.Hcl.Exceptions;

try
{
    HclFile.Load("name = \"unterminated"u8);
}
catch (HclSyntaxException ex)
{
    Console.WriteLine($"Syntax error at line {ex.Line}, col {ex.Column}: {ex.Message}");
}
```

| Exception | When |
|-----------|------|
| `HclSyntaxException` | Malformed input (unterminated strings, unexpected tokens) |
| `HclSemanticException` | Valid syntax but invalid semantics (duplicate attributes) |
| `MaxRecursionDepthExceededException` | Block nesting exceeds `MaxDepth` |
| `HclUnresolvableException` | Variable not found during evaluation |

All inherit from `HclException` → `Exception`.
