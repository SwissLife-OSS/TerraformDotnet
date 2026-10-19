namespace TerraformDotnet.Validation;

/// <summary>
/// A machine-readable rule recovered from a Terraform <c>validation</c> block. Constraints describe
/// what the variable value must look like <em>when a value is supplied</em>; a <c>var.x == null ||</c>
/// guard in the original condition is therefore not represented.
/// <para>
/// Extraction is purely structural and conservative: anything that is not recognised becomes an
/// <see cref="OpaqueConstraint"/> instead of a guessed rule. Every constraint keeps the author's
/// <see cref="ErrorMessage"/> and the HCL text it was derived from.
/// </para>
/// </summary>
public abstract record VariableConstraint
{
    private protected VariableConstraint(string errorMessage, string source, string? fieldName = null)
    {
        ErrorMessage = errorMessage;
        Source = source;
        FieldName = fieldName;
    }

    /// <summary>Gets the <c>error_message</c> of the validation block this constraint came from.</summary>
    public string ErrorMessage { get; init; }

    /// <summary>
    /// Gets the HCL text of the (logically normalised) sub-expression this constraint was derived from.
    /// </summary>
    public string Source { get; init; }

    /// <summary>
    /// Gets the name of the object attribute this constraint applies to (for <c>object({...})</c> or
    /// <c>list(object({...}))</c> variables), or <c>null</c> when the constraint applies to the variable or element as a whole.
    /// </summary>
    public string? FieldName { get; init; }
}

/// <summary>
/// The value must be one of a fixed set of literals. Ideal for rendering a dropdown (or, with
/// <see cref="ConstraintTarget.Elements"/>, a multi-select).
/// <example>
/// <code>
/// // condition = contains(["silver", "gold"], var.sla)
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Values">The allowed literals, in declaration order.</param>
/// <param name="Target">Whether the rule applies to the value or to each element.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record AllowedValuesConstraint(
    string ErrorMessage,
    string Source,
    IReadOnlyList<TerraformLiteral> Values,
    ConstraintTarget Target = ConstraintTarget.Value,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The value must not be any of a set of literals.
/// <example>
/// <code>
/// // condition = !contains(["admin", "root"], var.username)
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Values">The forbidden literals.</param>
/// <param name="Target">Whether the rule applies to the value or to each element.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record DisallowedValuesConstraint(
    string ErrorMessage,
    string Source,
    IReadOnlyList<TerraformLiteral> Values,
    ConstraintTarget Target = ConstraintTarget.Value,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The numeric value must lie within a range; either bound may be absent.
/// <example>
/// <code>
/// // condition = var.retention >= 1 &amp;&amp; var.retention &lt;= 35
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Min">The lower bound, or <c>null</c> when unbounded.</param>
/// <param name="MinInclusive">Whether <paramref name="Min"/> itself is allowed.</param>
/// <param name="Max">The upper bound, or <c>null</c> when unbounded.</param>
/// <param name="MaxInclusive">Whether <paramref name="Max"/> itself is allowed.</param>
/// <param name="Target">Whether the rule applies to the value or to each element.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record NumericRangeConstraint(
    string ErrorMessage,
    string Source,
    decimal? Min,
    bool MinInclusive,
    decimal? Max,
    bool MaxInclusive,
    ConstraintTarget Target = ConstraintTarget.Value,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The length must lie within inclusive bounds. It counts characters for strings and items for
/// lists/sets; the variable type tells which one applies.
/// <example>
/// <code>
/// // condition = length(var.name) >= 3 &amp;&amp; length(var.name) &lt;= 24
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Min">The inclusive minimum length, or <c>null</c>.</param>
/// <param name="Max">The inclusive maximum length, or <c>null</c>.</param>
/// <param name="Target">Whether the rule applies to the value or to each element.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record LengthConstraint(
    string ErrorMessage,
    string Source,
    int? Min,
    int? Max,
    ConstraintTarget Target = ConstraintTarget.Value,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The value must match a regular expression.
/// <example>
/// <code>
/// // condition = can(regex("^[a-z][a-z0-9-]*$", var.name))
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Regex">The pattern as written in HCL (RE2 syntax).</param>
/// <param name="Target">Whether the rule applies to the value or to each element.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record PatternConstraint(
    string ErrorMessage,
    string Source,
    string Regex,
    ConstraintTarget Target = ConstraintTarget.Value,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The string must start with a fixed prefix.
/// <example>
/// <code>
/// // condition = startswith(var.name, "app-")
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Text">The required prefix.</param>
/// <param name="Target">Whether the rule applies to the value or to each element.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record PrefixConstraint(
    string ErrorMessage,
    string Source,
    string Text,
    ConstraintTarget Target = ConstraintTarget.Value,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The string must end with a fixed suffix.
/// <example>
/// <code>
/// // condition = endswith(var.name, "-prod")
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Text">The required suffix.</param>
/// <param name="Target">Whether the rule applies to the value or to each element.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record SuffixConstraint(
    string ErrorMessage,
    string Source,
    string Text,
    ConstraintTarget Target = ConstraintTarget.Value,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The variable must not be <c>null</c>.
/// <example>
/// <code>
/// // condition = var.vault_id != null
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record NotNullConstraint(
    string ErrorMessage,
    string Source,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// The variable becomes required (must not be <c>null</c>) whenever <paramref name="Predicate"/> holds.
/// This is how a variable that has a <c>default = null</c> in Terraform can still be "required" for
/// certain values of another variable.
/// <example>
/// <code>
/// // variable "backup_vault_id" {
/// //   default = null
/// //   validation {
/// //     condition     = var.sla != "gold" || var.backup_vault_id != null
/// //     error_message = "backup_vault_id is required for the gold SLA."
/// //   }
/// // }
/// // => RequiredWhenConstraint(Predicate: sla == "gold")
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Predicate">The condition, over other variables, that makes this variable required.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record RequiredWhenConstraint(
    string ErrorMessage,
    string Source,
    VariablePredicate Predicate,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName);

/// <summary>
/// <paramref name="Inner"/> only has to hold whenever <paramref name="Predicate"/> holds.
/// <example>
/// <code>
/// // condition = var.sla != "gold" || var.replicas >= 3
/// // => ConditionalConstraint(Predicate: sla == "gold", Inner: NumericRange(Min: 3))
/// </code>
/// </example>
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
/// <param name="Predicate">The condition, over other variables, under which the rule applies.</param>
/// <param name="Inner">The rule that applies while the predicate holds.</param>
/// <param name="FieldName">The object attribute this rule applies to, or <c>null</c>.</param>
public sealed record ConditionalConstraint(
    string ErrorMessage,
    string Source,
    VariablePredicate Predicate,
    VariableConstraint Inner,
    string? FieldName = null) : VariableConstraint(ErrorMessage, Source, FieldName ?? Inner.FieldName);

/// <summary>
/// A validation that could not be classified. Nothing is guessed: consumers should surface
/// <see cref="VariableConstraint.ErrorMessage"/> as help text and leave enforcement to Terraform (or to a
/// full evaluator).
/// </summary>
/// <param name="ErrorMessage">The validation error message.</param>
/// <param name="Source">The originating HCL text.</param>
public sealed record OpaqueConstraint(string ErrorMessage, string Source) : VariableConstraint(ErrorMessage, Source);
