# TerraformDotnet 2.1.0

Both packages (`TerraformDotnet` and `TerraformDotnet.Hcl`) ship as 2.1.0.

## Highlights

- **Output that `terraform fmt` leaves alone.** `HclFileEmitter` and `ModuleCallEmitter` now produce text that
  `terraform fmt` does not change, for any tree, including multi-line values, comments, parentheses and operators.
  This is checked against `terraform fmt` by differential and fuzz tests.
- **`HclFormatter`.** A new public formatter applies the layout rules of `terraform fmt` (indentation, `=` alignment,
  comment alignment) to HCL text.
- **Comments everywhere.** Comments inside objects, tuples, function calls and block bodies are parsed, kept in the
  tree and written back.

## Breaking changes

### API

None. All changes to the public API are additions.

### Behavior

**`TerraformDotnet.Hcl` (emitter)**

The emitted text changes in these cases. All new output is what `terraform fmt` produces.

- `=` signs are aligned only across runs of adjacent single-line attributes. An attribute whose value spans several
  lines gets one space before `=` and starts a new run. Blank lines, comments and blocks also end a run.
- A tuple or call with a multi-line item is written one item per line. A call with exactly one multi-line argument
  keeps it inline: `merge(var.a, {` … `})`.
- An object-style `for` is written as `{ for ... }`.
- Parentheses are written where the tree needs them and nowhere else. `(1 + 2) * 3` used to be emitted as `1 + 2 * 3`,
  which changes its value.
- Like `terraform fmt`, there is no space between `in`/`if` and a following `(`, and there is a space after a leading
  `-` that follows `in`, `if` or `=>` (`k => - v`). This includes negative number literals built in code.
- Comments after a block's `}`, after tuple elements and call arguments, inside objects and before a closing bracket are
  now written. A tuple, call or object that holds a comment is written on several lines.
- String literals that contain a template with nested quotes (`"${join(", ", var.list)}"`) are written as they were
  parsed. They used to be escaped into invalid HCL.
- Characters outside the Basic Multilingual Plane (emoji) are written correctly. They used to be written as `��`.
- `Utf8HclWriter.WriteHashComment` and `WriteLineComment` (and so the emitter) throw `ArgumentException` when the
  comment text contains a line break, and `WriteBlockComment` throws when it contains `*/`. Such text used to be
  written as is, which ended the comment and turned the rest of the text into HCL. Parsed comments never contain these.
- When a tree built in code is not valid HCL (for example an attribute name with a space), the emitter writes it
  without formatting instead of throwing.
- Emitting is about 1.6–1.9× slower because of the extra formatting pass (a large file takes about 55 µs instead of
  30 µs). Parsing is unchanged.

**`TerraformDotnet` (`ModuleCallEmitter`)**

- Module arguments and `.tfvars` keys are aligned with the same run rules as above. Multi-line values are indented
  by the enclosing block only, as `terraform fmt` expects (they used to be indented to the `=` column).
- Variable declarations are aligned.
- An inline comment from `InputValue.Comment` follows the closing bracket of a multi-line value, and is written on its own
  line after the end marker of a heredoc.

## New features

### TerraformDotnet.Hcl

- `HclFormatter.Format(string)` and `HclFormatter.Format(ReadOnlySpan<byte>)` format HCL text. They throw
  `HclSyntaxException` when the text cannot be tokenized or its brackets do not balance.
- `HclBody.DanglingComments`: comments before a block's closing `}`.
- `HclObjectElement.LeadingComments` and `HclObjectElement.TrailingComment`.
- `DanglingComments` on `HclObjectExpression`, `HclTupleExpression` and `HclFunctionCallExpression`: comments before
  the closing bracket.
- A block's `TrailingComment` now holds the comment after its closing `}`, and tuple elements and call arguments carry
  their `LeadingComments` and `TrailingComment`.
- `HclFile.ToHcl()` and `HclExpression.ToHcl()` write a tree or an expression as formatted text.
- `HclFormatter.TryFormat(string, out string?)` returns `false` instead of throwing when the text is not valid HCL.
- Factories for building trees in code:
  - `HclLiteralExpression.String`, `Number` (`int`, `long`, `double`, `decimal`), `Bool` and `Null`. Numbers are written
    with the invariant culture and without an exponent; `NaN` and infinities throw.
  - `HclObjectExpression.Add(string key, HclExpression value)` writes the key bare when it is an identifier, and quoted
    otherwise (including `for`, `true`, `false` and `null`).
  - `HclComment.Hash`, `HclComment.Line` and `HclComment.Block`.

### Tests and tooling

- Tests compare the emitter and `HclFormatter` against `terraform fmt` 1.9.8 running in a
  `hashicorp/terraform` container started with [Squadron](https://github.com/SwissLife-OSS/squadron).
  They always run, so the test suites now need Docker.
- A seeded fuzz test checks that generated trees round-trip through the parser and are stable under `terraform fmt`.

## Known limits

- A comment on an opening line (`{ # note`) becomes a leading comment of the first element.
- Comments inside operators, conditionals, `for` expressions, index brackets and templates move to the next attribute.
- Blank lines around comments are not kept.
- The trailing comma after the last element of a multi-line tuple is not kept (the tree does not record it).
