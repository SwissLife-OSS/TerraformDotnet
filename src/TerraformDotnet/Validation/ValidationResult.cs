using TerraformDotnet.Module;

namespace TerraformDotnet.Validation;

/// <summary>
/// The result of evaluating a single <c>validation</c> block of a variable.
/// <example>
/// <code>
/// foreach (var result in report.Failures)
/// {
///     Console.WriteLine($"{result.VariableName}: {result.ErrorMessage}");
/// }
/// </code>
/// </example>
/// </summary>
/// <param name="VariableName">The variable that declares the validation.</param>
/// <param name="Index">The zero-based position of the validation within the variable.</param>
/// <param name="Validation">The evaluated <c>validation</c> block.</param>
/// <param name="Outcome">The verdict.</param>
/// <param name="ErrorMessage">
/// The rendered <c>error_message</c> when <paramref name="Outcome"/> is <see cref="ValidationOutcome.Failed"/>
/// (interpolations and <c>format(...)</c> are evaluated, surrounding whitespace such as the trailing newline of a
/// heredoc is trimmed); otherwise <c>null</c>.
/// </param>
/// <param name="Reason">Why the outcome is <see cref="ValidationOutcome.Indeterminate"/>; otherwise <c>null</c>.</param>
/// <param name="ReferencedVariables">The names of all variables the condition refers to, including the variable itself.</param>
public sealed record ValidationResult(
    string VariableName,
    int Index,
    TerraformValidation Validation,
    ValidationOutcome Outcome,
    string? ErrorMessage,
    string? Reason,
    IReadOnlyList<string> ReferencedVariables);
