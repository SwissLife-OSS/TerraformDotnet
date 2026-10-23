# TerraformDotnet 2.0.0

Both packages (`TerraformDotnet` and `TerraformDotnet.Hcl`) ship as 2.0.0.

## Highlights

- **Evaluate Terraform expressions.** The HCL evaluator now runs Terraform functions (through a pluggable
  `IHclFunctionResolver`), string templates, `can`/`try`, short-circuit logic and unknown-value propagation.
- **Terraform function library.** `TerraformFunctions` implements Terraform's deterministic functions and is checked against
  `terraform console` by a golden test suite.
- **Variable validation.** The new `TerraformDotnet.Validation` namespace extracts typed constraints from `validation`
  blocks (for building UIs) and evaluates every validation offline against entered values.

## Breaking changes

### API

| Before (1.x) | Now (2.0) | Migration |
|--------------|-----------|-----------|
| `TerraformVariable.Validation` (first validation block only) | Removed | Use `TerraformVariable.Validations` (`IReadOnlyList<TerraformValidation>`); `variable.Validations.FirstOrDefault()` is the old behavior |

### Behavior

**`TerraformDotnet` (module loader)**

- `TerraformVariable.IsNullable` now defaults to `true`, like Terraform. It is `false` only when the declaration says
  `nullable = false` (it used to be `false` unless `nullable = true` was written).
- A variable now keeps **all** of its `validation` blocks. Previously only the first block was read.
- A `validation` block whose `error_message` is not a plain string (an interpolation or a call such as `format(...)`)
  is now loaded. `TerraformValidation.ErrorMessage` holds the HCL source text in that case and
  `TerraformValidation.ErrorMessageExpression` the parsed expression. It used to throw `FormatException`.
  A missing `error_message` still throws `FormatException`.

**`TerraformDotnet.Hcl` (evaluator, parser)**

- `HclValue.ToHclString()` formats numbers the way Terraform does: shortest round-trip text, never in exponent notation
  (`+Inf`, `-Inf` and `NaN` for non-finite values). It used to use `double.ToString`.
- String literals containing `${...}` are evaluated as templates. A string that is a single interpolation
  (`"${var.count}"`) keeps the type of the interpolated value. `$${` and `%%{` are escapes. Template directives
  (`%{ if }`) evaluate to unknown.
- Operands are converted like Terraform does instead of throwing: numeric strings work in arithmetic and comparisons,
  and `"true"`/`"false"` work with `&&`, `||`, `!` and `?:`. Other types still throw `InvalidOperationException`
  (previously a non-`bool` operand of `&&`/`||`/`!`/`?:` was rejected, and so were numeric strings).
  Equality never converts.
- `&&` and `||` short-circuit with three-valued logic: `false && unknown` is `false`, `true || unknown` is `true`,
  anything else involving an unknown is unknown.
- Modulo by zero throws `InvalidOperationException`.
- `for` expressions over objects visit keys in lexical order (like Terraform), and an unknown `if` condition or unknown
  key/value makes the whole expression unknown instead of silently dropping the element.
- Function calls are delegated to the configured `IHclFunctionResolver`. With no resolver, or when the resolver returns
  `null`, or when an argument is unknown, the call is still `HclValue.Unknown(name, args)` as before.
- Exceeding the recursion depth now throws `HclEvaluationLimitException` (still an `InvalidOperationException`). It is
  not swallowed by `can(...)` / `try(...)`.
- The parser reads namespaced function names (`provider::azapi::parse_resource_id(...)`) as one function name.
- A heredoc no longer consumes the newline after its closing marker. Before, the next attribute name could be read as part
  of the heredoc expression.

## New features

### TerraformDotnet.Hcl

- `HclExpression.Parse(string)` and `HclExpression.TryParse(string?, out HclExpression?)` parse a standalone expression.
- `HclEvaluatorOptions` with `FunctionResolver`, `TreatUndefinedVariablesAsUnknown`, `MaxDepth` (default 128) and
  `MaxIterations` (default 100,000 `for` iterations per evaluation), passed to the new `HclEvaluator(HclEvaluatorOptions)`
  constructor. `new HclEvaluator()` keeps working with default options.
- `IHclFunctionResolver`, `HclFunctionException` (invalid function arguments; recoverable by `can`/`try`) and
  `HclEvaluationLimitException`.
- Built-in `can(expr)` and `try(expr, fallback, ...)` with lazy argument evaluation.
- The evaluator expands a final `...` argument (`f(list...)`) before calling the resolver; a non-tuple throws.

### TerraformDotnet

- `TerraformFunctions` (`IHclFunctionResolver`): `TerraformFunctions.Default`, `SupportedFunctions`, `IsSupported(name)`,
  `Invoke(name, arguments)`. Covers collection, string, numeric, conversion/encoding, `format`/`formatlist`, regex
  (RE2 semantics translated to .NET) and `cidr*` functions. Only deterministic, side-effect free functions are included.
- `TerraformTypeConverter.TryConvert` converts an `HclValue` to a `TerraformType` with Terraform's rules
  (`"5"` to `5`, sets sorted and de-duplicated, `optional(T, default)` filled in, undeclared object attributes dropped).
- `TerraformVariable.Validations` and `TerraformValidation.ErrorMessageExpression`.

### TerraformDotnet.Validation (new namespace)

- `ConstraintExtractor` (`Extract`, `ExtractAll`, `GetReferencedVariables`) reads validation blocks structurally, without
  evaluating them, into a typed model: `AllowedValuesConstraint`, `DisallowedValuesConstraint`, `NumericRangeConstraint`,
  `LengthConstraint`, `PatternConstraint`, `PrefixConstraint`, `SuffixConstraint`, `NotNullConstraint`,
  `RequiredWhenConstraint`, `ConditionalConstraint` and `OpaqueConstraint` (anything not recognized, shown as help text).
  Dropdowns, min/max and "required when" rules can be driven from it.
- `VariablePredicate` model (`ComparePredicate`, `InPredicate`, `IsNullPredicate`, `AndPredicate`, `OrPredicate`) and
  `VariablePredicateEvaluator.Evaluate` with three-valued logic (`bool?`).
- `ModuleValidator` evaluates every validation against supplied values (`HclValue`, `HclExpression` or a
  `TerraformModuleCall`) and returns a `ValidationReport`. Each `ValidationResult` is `Passed`, `Failed` or
  `Indeterminate`; it is only `Failed` when Terraform would definitely fail. The report also lists type errors and the
  variables that are required right now. Configure with `ModuleValidatorOptions` (`FunctionResolver`, `MaxDepth`,
  `MaxIterations`).
- `TerraformLiteral` value type for constants used by constraints and predicates.

### Tests and tooling

- Golden test suite generated with `terraform console` (`test/TerraformDotnet.Tests/__assets__/golden/generate.py`)
  covering the function library.

## Upgrade guide

```csharp
// 1.x
var validation = variable.Validation;

// 2.0
var validation = variable.Validations.FirstOrDefault();   // or iterate all of them
```

Things to check after upgrading:

1. Code that relied on `IsNullable == false` for variables without `nullable` now sees `true`.
2. Code that relied on `HclEvaluator` throwing for non-boolean `&&`/`||`/`?:` operands, for numeric strings in arithmetic,
   or on `${...}` inside quoted strings staying literal text.
3. Code that formatted numbers with `ToHclString()` and expected `double.ToString` output.
4. Catch blocks around the evaluator keep working: the new exceptions derive from `InvalidOperationException`.

## Known differences from Terraform

See [Known differences from Terraform](terraformdotnet.md#known-differences-from-terraform) for the documented deviations
of the offline validation evaluator.

**Full changelog**: https://github.com/SwissLife-OSS/TerraformDotnet/compare/1.4.0...2.0.0
