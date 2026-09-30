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
  - [Variable validation constraints](#variable-validation-constraints)
  - [Evaluating validations](#evaluating-validations)
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
| `IsNullable` | `bool` | Whether `null` is allowed. Defaults to `true` like Terraform; only `nullable = false` turns it off |
| `Validations` | `IReadOnlyList<TerraformValidation>` | All `validation` blocks in declaration order (Terraform allows several) |
| `Validation` | `TerraformValidation?` | The first validation block, or `null` (kept for convenience) |

### Variable validation constraints

`validation` blocks contain arbitrary Terraform expressions. `ConstraintExtractor` reads them **structurally** (it never
evaluates them) and turns the common idioms into a small typed model that a UI can render safely:
dropdowns for enums, min/max for ranges, and rules that make a variable required depending on other variables.

```csharp
var module = TerraformModule.LoadFromDirectory("./my-module");

foreach (var variable in module.Variables)
{
    foreach (var constraint in ConstraintExtractor.Extract(variable, module))
    {
        switch (constraint)
        {
            case AllowedValuesConstraint allowed:
                // condition = contains(["silver", "gold"], var.sla)  →  render a dropdown
                Console.WriteLine($"{variable.Name}: one of {string.Join(", ", allowed.Values.Select(v => v.Value))}");
                break;
            case NumericRangeConstraint range:
                Console.WriteLine($"{variable.Name}: {range.Min}..{range.Max}");
                break;
            case OpaqueConstraint opaque:
                // Not understood: show opaque.ErrorMessage as help text and let Terraform enforce it.
                Console.WriteLine($"{variable.Name}: {opaque.ErrorMessage}");
                break;
        }
    }
}
```

Pass the `module` so that `local.*` references (for example `contains(local.regions, var.region)`) can be resolved.
`ConstraintExtractor.ExtractAll(module)` returns the constraints of every variable by name.

Every validation block produces one or more constraints. Each keeps the block's `ErrorMessage` and the normalised
expression text in `Source`. The constraints of different blocks are never merged.

| Constraint | Recognised idioms |
|------------|-------------------|
| `AllowedValuesConstraint` | `contains([...], var.x)`, `var.x == "a" \|\| var.x == "b"`, `contains(local.list, var.x)`, `contains(keys(local.map), var.x)`, `contains(values({...}), var.x)` |
| `DisallowedValuesConstraint` | `!contains([...], var.x)`, `var.x != "a" && var.x != "b"`, `!(var.x == "a" \|\| ...)` |
| `NumericRangeConstraint` | `var.x >= 1 && var.x <= 35` (bounds can be inclusive or exclusive, and open on one side) |
| `LengthConstraint` | `length(var.x) >= 3 && length(var.x) <= 24`, `length(var.x) > 0` |
| `PatternConstraint` | `can(regex("^...$", var.x))`, `length(regexall("...", var.x)) > 0` |
| `PrefixConstraint` / `SuffixConstraint` | `startswith(var.x, "app-")`, `endswith(var.x, "-prod")` |
| `NotNullConstraint` | `var.x != null` |
| `RequiredWhenConstraint` | `var.sla != "gold" \|\| var.x != null` (also written as `?:` or `!(… && var.x == null)`) |
| `ConditionalConstraint` | `var.sla != "gold" \|\| var.replicas >= 3` — the inner constraint only applies when the predicate is true |
| `OpaqueConstraint` | Everything else. Never guessed, never dropped. |

Constraints on **list and set variables** use `Target = ConstraintTarget.Elements` and apply to each element, for example
`alltrue([for v in var.x : contains(["a", "b"], v)])` or `length(setsubtract(var.x, ["a", "b"])) == 0`.

A leading `var.x == null ||` guard is removed: constraints describe the rules for a value that has been provided.

#### Required and optional based on other variables

Terraform 1.9 allows a validation to reference other variables. The typical pattern is:

```hcl
variable "backup_vault_id" {
  type    = string
  default = null

  validation {
    condition     = var.sla != "gold" || var.backup_vault_id != null
    error_message = "backup_vault_id is required for the gold SLA."
  }
}
```

This becomes a `RequiredWhenConstraint` whose `Predicate` is `sla == "gold"`. Evaluate it with
`VariablePredicateEvaluator` against the values the user has entered so far:

```csharp
var vault = module.Variables.Single(v => v.Name == "backup_vault_id");
var required = ConstraintExtractor.Extract(vault, module).OfType<RequiredWhenConstraint>().Single();

var values = new Dictionary<string, TerraformLiteral>
{
    ["sla"] = TerraformLiteral.FromString("gold"),
};

bool? isRequired = VariablePredicateEvaluator.Evaluate(required.Predicate, values);
// true  → show as required
// false → show as optional
// null  → cannot be decided yet (for example "sla" has not been chosen)
```

The evaluator uses Terraform's three-valued logic: a variable missing from the dictionary is unknown,
`false && unknown` is `false` and `true || unknown` is `true`. Use `TerraformLiteral.Null` for a variable that is
known to be `null`. Equality between values of different types is `false`, ordering comparisons only work on numbers.

`ConstraintExtractor.GetReferencedVariables(variable)` lists the other variables a variable's validations depend on,
which tells a UI which fields to re-evaluate when a value changes.

A rule is attached to the variable whose `validation` block contains it. A rule written on the *driving* variable
(`var.sla != "gold" || var.backup_vault_id != null` inside `sla`) is therefore a `ConditionalConstraint` on `sla`;
it is not inverted onto `backup_vault_id`.

#### Limits

- Only top-level variables are modelled. Rules on attributes of object variables (`var.lock.kind`) are `OpaqueConstraint`.
- Predicates support comparisons with literals, `contains` over literal lists, `null` checks, boolean variables and
  `&&`/`||`/`!`. Comparing two variables, function calls on the left side (`lower(var.x) == "a"`) and other
  expressions produce an `OpaqueConstraint`.
- Nothing here replaces Terraform: the constraints drive the UI, `terraform plan` remains the source of truth.
  Use [`ModuleValidator`](#evaluating-validations) when you need every validation evaluated, not only the common idioms.

`error_message` may be a plain string, a heredoc, an interpolated string or any other expression such as
`format(...)`. `TerraformValidation.ErrorMessage` holds its HCL text, `ErrorMessageExpression` the parsed expression.

### Evaluating validations

`ConstraintExtractor` recognises idioms. `ModuleValidator` goes further: it **evaluates** every `validation` block against
the values entered so far, offline, with a restricted and side-effect free evaluator. Each validation ends up as one of

| Outcome | Meaning |
|---------|---------|
| `Passed` | The condition is `true`. |
| `Failed` | The condition is definitely `false`; `ErrorMessage` holds the rendered `error_message`. |
| `Indeterminate` | The condition cannot be decided offline; `Reason` says why. It is **never** reported as `Failed`. |

```csharp
var module = TerraformModule.LoadFromDirectory("./my-module");
var validator = new ModuleValidator(module);   // reusable and thread-safe

var report = validator.Validate(new Dictionary<string, HclValue>
{
    ["sla"] = HclValue.FromString("gold"),
    ["replicas"] = HclValue.FromNumber(2),
});

foreach (var failure in report.Failures)
{
    Console.WriteLine($"{failure.VariableName}: {failure.ErrorMessage}");
}

foreach (var open in report.Indeterminate)
{
    Console.WriteLine($"{open.VariableName}: not checked ({open.Reason})");
}

// Variables that have to be filled in right now, for example because "sla" is "gold".
foreach (var name in report.RequiredNow)
{
    Console.WriteLine($"{name} is required");
}
```

Values can also be given as HCL expressions (`Validate(IReadOnlyDictionary<string, HclExpression>)`, values that refer to
things outside the module are unknown) or as the arguments of a `TerraformModuleCall`.

How values are resolved, per variable:

1. A supplied value is used; a missing one falls back to the default (which is evaluated as well).
2. A required variable without a value has no value *yet*: validations that depend on it are `Indeterminate`,
   they do not fail.
3. The value is converted to the declared type with Terraform's rules (`"5"` → `5`, `1` → `"1"`, sets are sorted and
   de-duplicated, `optional(T, default)` attributes are filled in, undeclared object attributes are dropped). A value
   that cannot be converted ends up in `report.TypeErrors`, and the validations of that variable are `Indeterminate`.
4. An explicit `HclValue.Null` stays `null` for nullable variables and is replaced by the default when
   `nullable = false`.

`ValidationReport` members:

| Member | Description |
|--------|-------------|
| `Results`, `For(name)` | Every validation with its `VariableName`, `Index`, `Outcome`, `ErrorMessage`, `Reason` and `ReferencedVariables` |
| `Failures`, `Indeterminate`, `HasFailures`, `HasIndeterminate` | Filtered views |
| `Values` | The effective (defaulted and converted) value of every variable |
| `TypeErrors` | Variables whose supplied value does not match their type |
| `RequiredNow`, `IsRequiredNow(name)` | Variables without a default, plus variables with `default = null` that fail a validation when left `null` given the current values |

#### What is evaluated

The evaluator supports the Terraform expression language (operators, conditionals, `for` expressions, splat, index and
attribute access, string interpolation) with `var.*` and `local.*` references. Locals are resolved lazily and only when a
condition refers to them; cycles are `Indeterminate`.

Functions are limited to a deterministic whitelist (`TerraformFunctions.SupportedFunctions`). It is checked against
`terraform console` by a golden test suite; only `cidrcontains`, which newer Terraform versions add, is covered by unit
tests alone:

- Collections: `alltrue anytrue chunklist coalesce coalescelist compact concat contains distinct element flatten index keys
  length lookup matchkeys merge one range reverse setintersection setproduct setsubtract setunion slice sort sum
  transpose values zipmap`
- Strings: `chomp endswith format formatlist indent join lower regex regexall replace split startswith strcontains strrev
  substr title trim trimprefix trimspace trimsuffix upper`
- Numbers: `abs ceil floor log max min parseint pow signum`
- Conversion and encoding: `base64decode base64encode jsondecode jsonencode nonsensitive sensitive tobool tolist tomap
  tonumber toset tostring urlencode`
- Networking: `cidrcontains cidrhost cidrnetmask cidrsubnet cidrsubnets`
- `can` and `try` are part of the evaluator.

Anything else is **not** evaluated and makes the validation `Indeterminate`: functions with side effects or environment
access (`file`, `templatefile`, `timestamp`, `uuid`, `sha*`, …), `provider::…` functions, data sources, resources,
`path.*`, `terraform.*`, `each.*`, and template directives (`%{ if }`). Plug in your own functions with
`ModuleValidatorOptions.FunctionResolver` (an `IHclFunctionResolver`, for example one that delegates to
`TerraformFunctions.Default` first).

Limits protect the host from expensive conditions: `MaxDepth`, `MaxIterations` (all `for` loops together) and a 250 ms
timeout per regular expression. Exceeding one gives `Indeterminate`.

#### Known differences from Terraform

The result is only `Failed` when Terraform would definitely fail as well; when in doubt it is `Indeterminate`.

- `&&`, `||` and `?:` short-circuit. Terraform evaluates both operands, so a condition that errors in an operand Terraform
  would evaluate can be `Passed` here.
- Numbers are IEEE doubles; Terraform uses arbitrary precision. Values that cannot be reproduced exactly
  (for example `parseint` of huge numbers) are unknown, and `-0` is written as `0`.
- Regular expressions are translated from RE2 to .NET; unsupported syntax (`(?m)`, `(?U)`, look-around, back-references,
  Unicode script classes) and `format` verbs that are not implemented (`%e %g %c %U`) make the call unknown.
- `cidr*` functions reject leading zeros, IPv4-mapped and zoned addresses (unknown), and negative `cidrsubnet` arguments.
- A rendered `error_message` has its surrounding whitespace trimmed (the trailing newline of a heredoc), and `<<-` heredoc
  indentation is not removed.

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

### Module calls

`ModuleCalls` contains the `module` blocks declared by the loaded module. Each entry
represents a call to a child module, not the recursively loaded child module itself.
Terraform meta-arguments and child module input arguments are exposed separately while
their original HCL expressions are preserved.

```csharp
foreach (var call in module.ModuleCalls)
{
    Console.WriteLine($"module.{call.Name}");

    // Source, Version, Count, ForEach, DependsOn, and Providers are HCL expressions.
    Console.WriteLine(ModuleCallEmitter.EmitExpression(call.Source));

    foreach (var argument in call.Arguments)
    {
        Console.WriteLine($"  input: {argument.Key}");
    }
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

### Setting variables from HCL AST expressions

For complex or multi-line values, pass an `HclExpression` AST node directly.
The expression is formatted using the HCL emitter, producing canonical output
with proper indentation inside the module block:

```csharp
using TerraformDotnet.Hcl.Nodes;

// Simple variable reference
builder.Set("project", new HclAttributeAccessExpression
{
    Source = new HclVariableExpression { Name = "var" },
    Name = "project",
});
// Emits: project = var.project

// Function call
var mergeExpr = new HclFunctionCallExpression { Name = "merge" };
mergeExpr.Arguments.Add(new HclAttributeAccessExpression
{
    Source = new HclVariableExpression { Name = "var" },
    Name = "base_tags",
});
mergeExpr.Arguments.Add(new HclAttributeAccessExpression
{
    Source = new HclVariableExpression { Name = "local" },
    Name = "extra_tags",
});
builder.Set("tags", mergeExpr);
// Emits: tags = merge(var.base_tags, local.extra_tags)

// Multi-line: tuple of objects
var container = new HclObjectExpression();
container.Elements.Add(new HclObjectElement
{
    Key = new HclLiteralExpression { Value = "name", Kind = HclLiteralKind.String },
    Value = new HclLiteralExpression { Value = "web", Kind = HclLiteralKind.String },
});
container.Elements.Add(new HclObjectElement
{
    Key = new HclLiteralExpression { Value = "port", Kind = HclLiteralKind.String },
    Value = new HclLiteralExpression { Value = "8080", Kind = HclLiteralKind.Number },
});

var containers = new HclTupleExpression();
containers.Elements.Add(container);
builder.Set("containers", containers);
// Emits (multi-line, properly indented inside the module block):
//   containers = [{
//                   name = "web"
//                   port = 8080
//                 }]
```

### Emitting expressions standalone

`ModuleCallEmitter.EmitExpression` converts any `HclExpression` AST node to
a canonical HCL string:

```csharp
var expr = new HclFunctionCallExpression { Name = "flatten" };
expr.Arguments.Add(new HclTupleExpression());

string hcl = ModuleCallEmitter.EmitExpression(expr);
// "flatten([])"
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
