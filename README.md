# TerraformDotnet

A .NET toolkit for working with Terraform — parse HCL, load modules, inspect variables, and generate Terraform code.

[![NuGet](https://img.shields.io/nuget/v/TerraformDotnet.svg)](https://www.nuget.org/packages/TerraformDotnet)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![Built with GitHub Copilot](https://img.shields.io/badge/Built%20with-GitHub%20Copilot%20%C2%B7%20Claude%20Opus%204.6-8957e5?logo=githubcopilot)](https://github.com/features/copilot)

## Packages

| Package | Description |
|---------|-------------|
| **TerraformDotnet.Hcl** | High-performance HCLv2 parser and writer, AOT-compatible |
| **TerraformDotnet** | Terraform module loader and code generator built on top of TerraformDotnet.Hcl |

## Quick Start — TerraformDotnet

Load a Terraform module, inspect its variables, and generate the calling code.

### Install

```shell
dotnet add package TerraformDotnet
```

### Load and inspect a module

```csharp
using TerraformDotnet.Module;

var module = TerraformModule.LoadFromDirectory("/path/to/module");

Console.WriteLine($"Required variables: {module.RequiredVariables.Count}");
foreach (var v in module.RequiredVariables)
{
    Console.WriteLine($"  {v.Name}: {v.Type?.ToHcl() ?? "any"} — {v.Description}");
}

Console.WriteLine($"Optional variables: {module.OptionalVariables.Count}");
Console.WriteLine($"Outputs: {module.Outputs.Count}");
Console.WriteLine($"Resources: {module.Resources.Count}");
```

### Generate a module call

```csharp
using TerraformDotnet.Emit;

var call = new ModuleCallBuilder("my-app", module)
    .Source("git::https://example.com/modules/app?ref=v1.0")
    .FillRequired(name => $"var.{name}")
    .SetLiteral("tier", "premium")
    .IncludeOptionalComments(true)
    .Build();

var emitter = new ModuleCallEmitter(call);
string hcl = emitter.EmitModuleBlock();
// module "my-app" {
//   source = "git::https://example.com/modules/app?ref=v1.0"
//
//   project = var.project
//   region  = var.region
//   labels  = var.labels
//   tier    = "premium"
//
//   # (Optional) Maximum instance count.
//   # max_count = var.max_count
// }
```

### Generate multiple files at once

```csharp
emitter.WriteTo("output/", new FileEmitterOptions
{
    ModuleFileName = "resources-app.tf",
    VariablesFileName = "variables-app.tf",
    InputFiles = new Dictionary<string, IDictionary<string, InputValue>>
    {
        ["input-dev.tfvars"] = new Dictionary<string, InputValue>
        {
            ["project"] = new InputValue("\"dev-app\"", "Development"),
            ["region"] = "\"eu-west-1\"",
        },
        ["input-prod.tfvars"] = new Dictionary<string, InputValue>
        {
            ["project"] = new InputValue("\"prod-app\"", "Production"),
            ["region"] = "\"eu-west-1\"",
        },
    },
});
```

## Quick Start — TerraformDotnet.Hcl

Parse and write HCL at any level of abstraction.

### Install

```shell
dotnet add package TerraformDotnet.Hcl
```

### Parse HCL into an AST

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

HclBlock block = file.Body.Blocks[0];
Console.WriteLine(block.Type);       // "resource"
Console.WriteLine(block.Labels[0]);  // "aws_instance"
Console.WriteLine(block.Labels[1]);  // "web"
```

### Write HCL

```csharp
using TerraformDotnet.Hcl.Writer;

var buffer = new ArrayBufferWriter<byte>();
using var writer = new Utf8HclWriter(buffer);

writer.WriteBlockStart("resource", "aws_s3_bucket", "data");
writer.WriteAttribute("bucket", "my-bucket");
writer.WriteBooleanAttribute("force_destroy", true);
writer.WriteBlockEnd();
writer.Flush();

string output = Encoding.UTF8.GetString(buffer.WrittenSpan);
// resource "aws_s3_bucket" "data" {
//   bucket        = "my-bucket"
//   force_destroy = true
// }
```

### Round-trip (parse → modify → emit)

```csharp
var hcl = "name = \"old\""u8;
HclFile file = HclFile.Load(hcl);

file.Body.Attributes[0].Value = new HclLiteralExpression("new");

var buffer = new ArrayBufferWriter<byte>();
using var writer = new Utf8HclWriter(buffer);
new HclFileEmitter(writer).Emit(file);
writer.Flush();
// Output: name = "new"
```

## Architecture

| Layer | Namespace | Description |
|-------|-----------|-------------|
| **Reader** | `TerraformDotnet.Hcl.Reader` | `ref struct` tokenizer — zero-copy, pull-based |
| **Nodes** | `TerraformDotnet.Hcl.Nodes` | AST model with visitor pattern and `DeepClone()` |
| **Writer** | `TerraformDotnet.Hcl.Writer` | Forward-only writer producing `terraform fmt` output |
| **Evaluation** | `TerraformDotnet.Hcl.Evaluation` | Variable resolution engine |
| **Module** | `TerraformDotnet.Module` | Terraform module loader and model |
| **Emit** | `TerraformDotnet.Emit` | Module call builder and code emitter |

See [docs/hcl.md](docs/hcl.md) and [docs/terraformdotnet.md](docs/terraformdotnet.md) for detailed documentation.

See [docs/architecture.md](docs/architecture.md) for internal design and architecture.

## Building

```shell
dotnet build all.csproj
```

## Testing

```shell
dotnet test all.csproj
```

## Performance

TerraformDotnet.Hcl uses stack-first, zero-copy design:

- `Utf8HclReader` is a `ref struct` operating on `ReadOnlySpan<byte>`
- Token values reference slices of the original input — no allocations for most tokens
- No LINQ, no reflection in any hot path
- AOT-compatible (`IsAotCompatible = true`)

## License

[MIT](LICENSE)