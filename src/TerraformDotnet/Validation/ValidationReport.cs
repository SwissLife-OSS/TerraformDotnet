using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Validation;

/// <summary>
/// The outcome of validating a set of variable values against a module's <c>validation</c> blocks.
/// <example>
/// <code>
/// var report = new ModuleValidator(module).Validate(values);
/// if (report.HasFailures)
/// {
///     foreach (var failure in report.Failures)
///     {
///         Console.WriteLine($"{failure.VariableName}: {failure.ErrorMessage}");
///     }
/// }
/// </code>
/// </example>
/// </summary>
public sealed class ValidationReport
{
    private readonly Dictionary<string, List<ValidationResult>> _byVariable;

    internal ValidationReport(
        IReadOnlyList<ValidationResult> results,
        IReadOnlyDictionary<string, HclValue> values,
        IReadOnlyDictionary<string, string> typeErrors,
        IReadOnlyList<string> requiredNow)
    {
        Results = results;
        Values = values;
        TypeErrors = typeErrors;
        RequiredNow = requiredNow;

        _byVariable = new Dictionary<string, List<ValidationResult>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (!_byVariable.TryGetValue(result.VariableName, out var list))
            {
                list = [];
                _byVariable[result.VariableName] = list;
            }

            list.Add(result);
        }
    }

    /// <summary>Gets the result of every <c>validation</c> block, in module declaration order.</summary>
    public IReadOnlyList<ValidationResult> Results { get; }

    /// <summary>Gets the results whose condition definitely evaluated to <c>false</c>.</summary>
    public IEnumerable<ValidationResult> Failures => Results.Where(r => r.Outcome == ValidationOutcome.Failed);

    /// <summary>Gets the results that could not be evaluated offline.</summary>
    public IEnumerable<ValidationResult> Indeterminate => Results.Where(r => r.Outcome == ValidationOutcome.Indeterminate);

    /// <summary>Gets whether at least one validation definitely failed.</summary>
    public bool HasFailures => Results.Any(r => r.Outcome == ValidationOutcome.Failed);

    /// <summary>Gets whether at least one validation could not be evaluated offline.</summary>
    public bool HasIndeterminate => Results.Any(r => r.Outcome == ValidationOutcome.Indeterminate);

    /// <summary>
    /// Gets the effective value of every variable after applying defaults and type conversion.
    /// A variable that is required but has no value yet, or whose value does not fit its type,
    /// is an unknown value.
    /// </summary>
    public IReadOnlyDictionary<string, HclValue> Values { get; }

    /// <summary>
    /// Gets the variables whose supplied value does not conform to the declared type, with the
    /// reason. Terraform rejects such values before running any validation; their validations, and
    /// those of variables that refer to them, are indeterminate.
    /// </summary>
    public IReadOnlyDictionary<string, string> TypeErrors { get; }

    /// <summary>
    /// Gets the variables that need a non-null value with the current values: those without a
    /// default, and those with a <c>null</c> default whose validations fail when the variable is left
    /// <c>null</c> (for example <c>var.sla != "gold" || var.backup_vault_id != null</c> while
    /// <c>sla</c> is <c>"gold"</c>).
    /// </summary>
    public IReadOnlyList<string> RequiredNow { get; }

    /// <summary>Gets the results of one variable's validation blocks.</summary>
    /// <param name="variableName">The variable name.</param>
    /// <returns>The results in declaration order; empty when the variable has no validations.</returns>
    public IReadOnlyList<ValidationResult> For(string variableName)
        => _byVariable.TryGetValue(variableName, out var list) ? list : [];

    /// <summary>Determines whether the variable needs a non-null value with the current values.</summary>
    /// <param name="variableName">The variable name.</param>
    /// <returns><c>true</c> when the variable is listed in <see cref="RequiredNow"/>.</returns>
    public bool IsRequiredNow(string variableName) => RequiredNow.Contains(variableName, StringComparer.Ordinal);
}
