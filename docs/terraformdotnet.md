# TerraformDotnet — Usage Guide

Load Terraform modules, inspect their structure, and generate module calling code.

## Table of Contents

- [Installation](#installation)
- [Loading a Module](#loading-a-module)
  - [From a directory](#from-a-directory)
  - [From parsed HCL files](#from-parsed-hcl-files)
  - [From inline content](#from-inline-content)
- [Inspecting Module Structure](#inspecting-module-structure)
  - [Variables](#variables)
  - [Type system](#type-system)
  - [Outputs](#outputs)
  - [Resources and data sources](#resources-and-data-sources)
  - [Locals](#locals)
  - [Provider requirements](#provider-requirements)
- [Building a Module Call](#building-a-module-call)
  - [Basic usage](#basic-usage)
  - [Setting variables](#setting-variables)
  - [Auto-filling required variables](#auto-filling-required-variables)
  - [Sentinel default variables](#sentinel-default-variables)
  - [Optional variable comments](#optional-variable-comments)
  - [Meta-arguments](#meta-arguments)
  - [Validation](#validation)
- [Emitting Code](#emitting-code)
  - [Module block](#module-block)
  - [Variable declarations](#variable-declarations)
  - [Input values (tfvars)](#input-values-tfvars)
  - [Writing files to disk](#writing-files-to-disk)
- [End-to-End Example](#end-to-end-example)

---

## Installation

```shell
dotnet add package TerraformDotnet
```

This transitively brings in `TerraformDotnet.Hcl`. Requires .NET 10+.

---

## Loading a Module

### From a directory

The primary entry point. Reads all `*.tf` files in a directory (non-recursive), parses each, and merges the results:

```csharp
using TerraformDotnet.Module;

var module = TerraformModule.LoadFromDirectory("/path/to/terraform-module");
```

This mirrors how Terraform itself treats a directory — all `.tf` files in the same directory belong to the same module.

### From parsed HCL files

If you've already parsed the HCL files:

```csharp
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

var files = new List<HclFile>
{
    HclFile.Load(File.ReadAllBytes("variables.tf")),
    HclFile.Load(File.ReadAllBytes("resources.tf")),
    HclFile.Load(File.ReadAllBytes("outputs.tf")),
};

var module = TerraformModule.LoadFromFiles(files);
```

### From inline content

Convenient for testing and one-off parsing:

```csharp
using System.Text;
using TerraformDotnet.Module;

var module = TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes("""
    variable "name" {
      type        = string
      description = "The resource name."
    }

    variable "region" {
      type    = string
      default = "us-east-1"
    }

    output "id" {
      value = resource.main.id
    }
    """));
```

---

## Inspecting Module Structure

### Variables

Variables are classified as required (no default) or optional (has a default):

```csharp
var module = TerraformModule.LoadFromDirectory("./my-module");

Console.WriteLine($"Total variables: {module.Variables.Count}");
Console.WriteLine($"Required: {module.RequiredVariables.Count}");
Console.WriteLine($"Optional: {module.OptionalVariables.Count}");

foreach (var v in module.RequiredVariables)
{
    Console.WriteLine($"  {v.Name}: {v.Type?.ToHcl() ?? "any"}");
    Console.WriteLine($"    Description: {v.Description ?? "(none)"}");
    Console.WriteLine($"    Sensitive: {v.IsSensitive}");
    Console.WriteLine($"    Nullable: {v.IsNullable}");
}

foreach (var v in module.OptionalVariables)
{
    Console.WriteLine($"  {v.Name}: {v.Type?.ToHcl() ?? "any"} (default provided)");
}
```

Variable properties:

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Variable name |
| `Type` | `TerraformType?` | Parsed type constraint (`null` = any) |
| `Description` | `string?` | Human-readable description |
| `Default` | `HclExpression?` | Default value expression (`null` = required) |
| `IsRequired` | `bool` | `true` when no default |
| `IsOptional` | `bool` | `true` when default is set |
| `HasSentinelDefault(sentinel)` | `bool` | `true` when default is a string matching the sentinel |
| `IsSensitive` | `bool` | Marks sensitive values |
| `IsNullable` | `bool` | Whether `null` is allowed |
| `Validation` | `TerraformValidation?` | Validation rule with condition and error message |

### Type system

Terraform type constraints are parsed into a structured model:

```csharp
using TerraformDotnet.Types;

foreach (var v in module.Variables)
{
    if (v.Type is null) continue;

    switch (v.Type)
    {
        case { Kind: TerraformTypeKind.String }:
            Console.WriteLine($"{v.Name}: string");
            break;

        case TerraformCollectionType { Kind: TerraformTypeKind.List } list:
            Console.WriteLine($"{v.Name}: list({list.Element.ToHcl()})");
            break;

        case TerraformCollectionType { Kind: TerraformTypeKind.Map } map:
            Console.WriteLine($"{v.Name}: map({map.Element.ToHcl()})");
            break;

        case TerraformObjectType obj:
            Console.WriteLine($"{v.Name}: object with {obj.Fields.Count} fields");
            foreach (var field in obj.Fields)
            {
                var opt = field.IsOptional ? " (optional)" : "";
                Console.WriteLine($"  .{field.Name}: {field.Type.ToHcl()}{opt}");
            }
            break;
    }
}
```

Supported type kinds:

| Kind | Examples | Class |
|------|----------|-------|
| `String`, `Number`, `Bool`, `Any` | `string`, `number`, `bool`, `any` | Primitive singletons via `TerraformType.String` etc. |
| `List`, `Set`, `Map` | `list(string)`, `set(number)`, `map(any)` | `TerraformCollectionType` with `Element` |
| `Object` | `object({ name = string, age = optional(number, 0) })` | `TerraformObjectType` with `Fields` |
| `Tuple` | `tuple([string, number])` | `TerraformTupleType` with `Elements` |

Types can be constructed programmatically and emitted back to HCL:

```csharp
var type = TerraformType.Map(TerraformType.List(TerraformType.String));
Console.WriteLine(type.ToHcl()); // "map(list(string))"
```

### Outputs

```csharp
foreach (var output in module.Outputs)
{
    Console.WriteLine($"output \"{output.Name}\"");
    Console.WriteLine($"  Description: {output.Description ?? "(none)"}");
    Console.WriteLine($"  Sensitive: {output.IsSensitive}");

    if (output.DependsOn is not null)
    {
        Console.WriteLine($"  DependsOn: {string.Join(", ", output.DependsOn)}");
    }
}
```

### Resources and data sources

```csharp
foreach (var resource in module.Resources)
{
    Console.WriteLine($"resource \"{resource.Type}\" \"{resource.Name}\"");

    if (resource.Count is not null)
        Console.WriteLine("  Has count");
    if (resource.ForEach is not null)
        Console.WriteLine("  Has for_each");
    if (resource.Provider is not null)
        Console.WriteLine($"  Provider: {resource.Provider}");
    if (resource.DependsOn is not null)
        Console.WriteLine($"  DependsOn: {string.Join(", ", resource.DependsOn)}");

    // Access the full body AST for deep inspection
    foreach (var attr in resource.Body.Attributes)
    {
        Console.WriteLine($"  {attr.Name} = ...");
    }
}

foreach (var data in module.DataSources)
{
    Console.WriteLine($"data \"{data.Type}\" \"{data.Name}\"");
}
```

### Locals

```csharp
foreach (var local in module.Locals)
{
    Console.WriteLine($"local.{local.Name}");
    // local.Value is an HclExpression — inspect or evaluate it
}
```

### Provider requirements

```csharp
if (module.RequiredTerraformVersion is not null)
{
    Console.WriteLine($"Required Terraform: {module.RequiredTerraformVersion}");
}

foreach (var provider in module.ProviderRequirements)
{
    Console.WriteLine($"Provider: {provider.Name}");
    Console.WriteLine($"  Source: {provider.Source ?? "(default)"}");
    Console.WriteLine($"  Version: {provider.Version ?? "(any)"}");
}
```

---

## Building a Module Call

`ModuleCallBuilder` constructs an immutable `ModuleCall` that describes how to call a module.

### Basic usage

```csharp
using TerraformDotnet.Emit;

var call = new ModuleCallBuilder("my-app")
    .Source("./modules/app")
    .Set("name", "var.app_name")
    .Set("region", "var.region")
    .Build();
```

### Setting variables

Three ways to set variable values:

```csharp
var builder = new ModuleCallBuilder("my-app")
    .Source("./modules/app");

// Raw HCL expression — passed through as-is
builder.Set("tags", "var.common_tags");
builder.Set("config", "merge(var.base_config, local.overrides)");

// String literal — automatically quoted
builder.SetLiteral("environment", "production");
// Emits: environment = "production"

// Numeric and boolean literals
builder.SetLiteral("instance_count", 3);
builder.SetLiteral("enable_monitoring", true);
// Emits: instance_count = 3
// Emits: enable_monitoring = true
```

### Auto-filling required variables

When built with a `TerraformModule`, required variables can be auto-filled:

```csharp
var module = TerraformModule.LoadFromDirectory("./modules/app");

var call = new ModuleCallBuilder("my-app", module)
    .Source("git::https://example.com/modules/app?ref=v2.0")
    .FillRequired(name => $"var.{name}")  // var.project, var.region, etc.
    .Build();
```

`FillRequired` skips variables already set, so you can override specific ones:

```csharp
var call = new ModuleCallBuilder("my-app", module)
    .Source("./modules/app")
    .SetLiteral("region", "eu-west-1")   // Override this one
    .FillRequired(name => $"var.{name}") // Fill the rest
    .Build();
```

### Sentinel default variables

Some modules use a sentinel default value (e.g. `"inject-at-runtime"`) for variables whose real values are supplied externally — typically via `TF_VAR_*` environment variables set by a CI/CD pipeline or secrets manager. These variables are technically optional (they have a default), but the sentinel is not a real value.

`FillSentinel` treats these like required variables: it adds them to the module call arguments and also tracks them so `EmitInputValues` can render them as commented-out `.tfvars` entries:

```csharp
var module = TerraformModule.LoadFromDirectory("./modules/app");

// Discover which variables use the sentinel convention
foreach (var v in module.GetSentinelVariables("inject-at-runtime"))
{
    Console.WriteLine($"{v.Name} is supplied externally");
}

// Build the call — sentinel vars are wired up and tracked
var call = new ModuleCallBuilder("my-app", module)
    .Source("git::https://example.com/modules/app?ref=v2.0")
    .FillRequired(name => $"var.{name}")
    .FillSentinel("inject-at-runtime", name => $"var.{name}")
    .Build();
```

`FillSentinel` skips variables already set (just like `FillRequired`), and sentinel variables are excluded from `IncludeOptionalComments` since they are already in the arguments.

In the emitted `.tfvars`, sentinel variables appear as commented-out entries with their description:

```hcl
# (Required) Project name.
project = "my-app"

# The service token.
# service_token = ""
# The access key.
# access_key    = ""
```

This makes it clear that these variables exist and are expected to be supplied externally.

### Optional variable comments

Include commented-out optional variables in the emitted module block:

```csharp
var call = new ModuleCallBuilder("my-app", module)
    .Source("./modules/app")
    .FillRequired(name => $"var.{name}")
    .IncludeOptionalComments(true)
    .Build();

// Optional variables not explicitly set will appear as:
//   # (Optional) Enable high availability.
//   # enable_ha = var.enable_ha
```

### Meta-arguments

```csharp
var call = new ModuleCallBuilder("worker", module)
    .Source("./modules/worker")
    .FillRequired(name => $"var.{name}")
    .Version("~> 2.0")
    .Count("var.worker_count")
    // OR: .ForEach("var.environments")
    .DependsOn("module.network", "module.security")
    .Providers(new Dictionary<string, string>
    {
        ["aws"] = "aws.west",
    })
    .Build();
```

### Validation

When built with a module, `Build()` validates that all required variables are set:

```csharp
var module = TerraformModule.LoadFromDirectory("./modules/app");

// This throws InvalidOperationException listing missing variables
var call = new ModuleCallBuilder("my-app", module)
    .Source("./modules/app")
    .Set("project", "var.project")
    // Missing: region, labels
    .Build();
// InvalidOperationException: Missing required variables: region, labels.
```

Building without a module skips validation entirely:

```csharp
var call = new ModuleCallBuilder("my-app")
    .Source("./modules/app")
    .Set("anything", "any_expression")
    .Build(); // No validation
```

---

## Emitting Code

`ModuleCallEmitter` renders a `ModuleCall` as Terraform code.

### Module block

```csharp
var emitter = new ModuleCallEmitter(call);
string hcl = emitter.EmitModuleBlock();
```

Output:

```hcl
module "my-app" {
  source  = "git::https://example.com/modules/app?ref=v2.0"
  version = "~> 2.0"

  project     = var.project
  region      = var.region
  labels      = var.labels
  environment = "production"

  # (Optional) Enable high availability.
  # enable_ha = var.enable_ha

  # (Optional) Maximum instance count.
  # max_count = var.max_count

  depends_on = [module.network, module.security]
}
```

Arguments are automatically aligned (matching `terraform fmt`). Sections are separated by blank lines in this order: source/version, arguments, commented optionals, meta-arguments.

### Variable declarations

Generate pass-through variable declarations for the calling module:

```csharp
// Minimal — empty variable blocks
string vars = emitter.EmitVariableDeclarations();
```

```hcl
variable "project" {}

variable "region" {}

variable "labels" {}
```

Include type, description, and default from the source module:

```csharp
string vars = emitter.EmitVariableDeclarations(new VariableDeclarationOptions
{
    IncludeType = true,
    IncludeDescription = true,
    IncludeDefault = true,
});
```

```hcl
variable "project" {
  type        = string
  description = "The project name."
}

variable "region" {
  type        = string
  description = "Deployment region."
}

variable "labels" {
  type        = map(string)
  description = "Resource labels."
}
```

### Input values (tfvars)

Generate `.tfvars` files with concrete values:

```csharp
var values = new Dictionary<string, InputValue>
{
    ["project"] = new InputValue("\"web-portal\"", "JIRA-1234"),
    ["region"] = "\"eu-west-1\"",           // implicit conversion, no comment
    ["labels"] = "{ team = \"platform\" }",
};

string tfvars = emitter.EmitInputValues(values);
```

```hcl
# (Required) The project name.
project = "web-portal" # JIRA-1234

# (Required) Deployment region.
region  = "eu-west-1"

# (Required) Resource labels.
labels  = { team = "platform" }
```

When a source module is available, variable descriptions are included as comments with `(Required)` / `(Optional)` labels. Keys are aligned. Inline comments from `InputValue.Comment` appear after the value.

### Writing files to disk

Generate all files at once:

```csharp
emitter.WriteTo("output/", new FileEmitterOptions
{
    ModuleFileName = "resources-app.tf",       // default: "resources-{name}.tf"
    VariablesFileName = "variables-app.tf",    // null to skip
    InputFiles = new Dictionary<string, IDictionary<string, InputValue>>
    {
        ["input-dev.tfvars"] = devValues,
        ["input-staging.tfvars"] = stagingValues,
        ["input-prod.tfvars"] = prodValues,
    },
});
```

This creates the output directory if it doesn't exist and writes:
- `resources-app.tf` — the module block
- `variables-app.tf` — pass-through variable declarations
- `input-dev.tfvars`, `input-staging.tfvars`, `input-prod.tfvars` — per-environment input values

---

## End-to-End Example

Complete workflow: load a module, build a call, and generate all output files.

```csharp
using TerraformDotnet.Emit;
using TerraformDotnet.Module;

// 1. Load the module
var module = TerraformModule.LoadFromDirectory("./modules/compute");

// 2. Display module info
Console.WriteLine($"Module has {module.RequiredVariables.Count} required variables:");
foreach (var v in module.RequiredVariables)
{
    Console.WriteLine($"  {v.Name}: {v.Type?.ToHcl() ?? "any"}");
}

// 3. Build the module call
var call = new ModuleCallBuilder("compute", module)
    .Source("git::https://example.com/modules/compute?ref=v3.2")
    .Version("~> 3.0")
    .FillRequired(name => $"var.{name}")
    .SetLiteral("environment", "production")
    .IncludeOptionalComments(true)
    .Build();

// 4. Create the emitter
var emitter = new ModuleCallEmitter(call);

// 5. Preview the module block
Console.WriteLine(emitter.EmitModuleBlock());

// 6. Preview variable declarations with full metadata
Console.WriteLine(emitter.EmitVariableDeclarations(new VariableDeclarationOptions
{
    IncludeType = true,
    IncludeDescription = true,
}));

// 7. Write everything to disk
emitter.WriteTo("output/compute/", new FileEmitterOptions
{
    ModuleFileName = "resources-compute.tf",
    VariablesFileName = "variables-compute.tf",
    InputFiles = new Dictionary<string, IDictionary<string, InputValue>>
    {
        ["input-dev.tfvars"] = new Dictionary<string, InputValue>
        {
            ["project_name"] = new InputValue("\"dev-portal\"", "Development"),
            ["region"] = "\"us-east-1\"",
            ["owner"] = "\"dev-team\"",
            ["labels"] = "{}",
        },
        ["input-prod.tfvars"] = new Dictionary<string, InputValue>
        {
            ["project_name"] = new InputValue("\"prod-portal\"", "Production"),
            ["region"] = "\"eu-west-1\"",
            ["owner"] = "\"sre-team\"",
            ["labels"] = "{ environment = \"production\" }",
        },
    },
});

Console.WriteLine("Generated files in output/compute/");
```
