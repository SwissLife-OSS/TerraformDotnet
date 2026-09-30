using TerraformDotnet.Evaluation;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Validation;

/// <summary>
/// Evaluates the <c>validation</c> blocks of a Terraform module against a set of variable values,
/// without running Terraform.
/// </summary>
/// <remarks>
/// <para>
/// The validator follows Terraform's pipeline: values that are not supplied fall back to the
/// variable default, every value is converted to the declared type (<see cref="TerraformTypeConverter"/>),
/// and then every condition is evaluated with <c>var.*</c> (all variables) and <c>local.*</c>
/// available. Like Terraform, validations also run for <c>null</c> values.
/// </para>
/// <para>
/// A validation is reported as <see cref="ValidationOutcome.Failed"/> only when its condition
/// definitely evaluates to <c>false</c>. Whatever cannot be evaluated offline — a variable that is
/// required but has no value yet, references to resources or data sources, unsupported functions,
/// values that do not match the declared type — makes it <see cref="ValidationOutcome.Indeterminate"/>
/// instead, so a valid configuration is never rejected because of a limitation of this evaluator.
/// The evaluation never throws for module content.
/// </para>
/// <para>An instance is immutable and may be shared between threads.</para>
/// <example>
/// <code>
/// var module = TerraformModule.LoadFromDirectory("./modules/database");
/// var validator = new ModuleValidator(module);
///
/// var report = validator.Validate(new Dictionary&lt;string, HclValue&gt;
/// {
///     ["sla"] = HclValue.FromString("platinum"),
/// });
///
/// foreach (var failure in report.Failures)
/// {
///     Console.WriteLine($"{failure.VariableName}: {failure.ErrorMessage}");
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class ModuleValidator
{
    private readonly TerraformModule _module;
    private readonly ModuleValidatorOptions _options;
    private readonly ModuleLocals _locals;
    private readonly Dictionary<TerraformValidation, string[]> _localReferences = [];

    /// <summary>Initializes a validator for a module.</summary>
    /// <param name="module">The module whose variables are validated.</param>
    /// <param name="options">Optional evaluation settings.</param>
    public ModuleValidator(TerraformModule module, ModuleValidatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        _module = module;
        _options = options ?? new ModuleValidatorOptions();
        _locals = new ModuleLocals(module);

        foreach (var variable in module.Variables)
        {
            foreach (var validation in variable.Validations)
            {
                var text = ModuleLocals.TextOf(validation.Condition) + " " + ModuleLocals.TextOf(validation.ErrorMessageExpression);
                _localReferences[validation] = _locals.FindReferences(text).Distinct(StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>Validates variable values.</summary>
    /// <param name="values">
    /// The supplied values by variable name. A variable that is missing uses its default; a required
    /// variable that is missing has no value yet, which makes validations that depend on it indeterminate.
    /// Pass <see cref="HclValue.Null"/> for an explicit <c>null</c>.
    /// </param>
    /// <returns>The report.</returns>
    public ValidationReport Validate(IReadOnlyDictionary<string, HclValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return ValidateCore(values, CreateEvaluator());
    }

    /// <summary>
    /// Validates variable values given as HCL expressions, for example the arguments of a module call
    /// that were parsed with <see cref="HclExpression.Parse(string)"/>. Expressions that refer to
    /// values outside the module (such as <c>var.x</c> of the calling module) are unknown values.
    /// </summary>
    /// <param name="values">The supplied value expressions by variable name.</param>
    /// <returns>The report.</returns>
    public ValidationReport Validate(IReadOnlyDictionary<string, HclExpression> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var evaluator = CreateEvaluator();
        var evaluated = new Dictionary<string, HclValue>(values.Count, StringComparer.Ordinal);
        foreach (var (name, expression) in values)
        {
            evaluated[name] = EvaluateStandalone(expression, evaluator, $"var.{name}");
        }

        return ValidateCore(evaluated, evaluator);
    }

    /// <summary>Validates the arguments of a module call against the module's validations.</summary>
    /// <param name="call">A call whose arguments are the variable values.</param>
    /// <returns>The report.</returns>
    public ValidationReport Validate(TerraformModuleCall call)
    {
        ArgumentNullException.ThrowIfNull(call);

        return Validate(call.Arguments);
    }

    private HclEvaluator CreateEvaluator() => new(new HclEvaluatorOptions
    {
        FunctionResolver = _options.FunctionResolver ?? TerraformFunctions.Default,
        TreatUndefinedVariablesAsUnknown = true,
        MaxDepth = _options.MaxDepth,
        MaxIterations = _options.MaxIterations,
    });

    private static HclValue EvaluateStandalone(HclExpression expression, HclEvaluator evaluator, string description)
    {
        try
        {
            return evaluator.Evaluate(expression, new HclEvaluationContext());
        }
        catch (Exception)
        {
            return HclValue.Unknown(description);
        }
    }

    private ValidationReport ValidateCore(IReadOnlyDictionary<string, HclValue> supplied, HclEvaluator evaluator)
    {
        var typeErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = ResolveValues(supplied, evaluator, typeErrors);

        var results = new List<ValidationResult>();
        foreach (var variable in _module.Variables)
        {
            for (var index = 0; index < variable.Validations.Count; index++)
            {
                results.Add(Evaluate(variable, index, values, typeErrors, evaluator));
            }
        }

        var required = FindRequiredNow(values, typeErrors, evaluator);

        return new ValidationReport(results, values, typeErrors, required);
    }

    private Dictionary<string, HclValue> ResolveValues(
        IReadOnlyDictionary<string, HclValue> supplied,
        HclEvaluator evaluator,
        Dictionary<string, string> typeErrors)
    {
        var values = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        foreach (var variable in _module.Variables)
        {
            var name = variable.Name;
            var raw = supplied.TryGetValue(name, out var given)
                ? given
                : variable.Default is null
                    ? HclValue.Unknown($"var.{name} (no value yet)")
                    : EvaluateStandalone(variable.Default, evaluator, $"var.{name} (default)");

            if (raw.Type == HclValueType.Null && !variable.IsNullable)
            {
                if (variable.Default is null)
                {
                    typeErrors[name] = "null is not allowed because the variable is declared with nullable = false.";
                    values[name] = HclValue.Unknown($"var.{name} (invalid)");

                    continue;
                }

                raw = EvaluateStandalone(variable.Default, evaluator, $"var.{name} (default)");
            }

            if (TerraformTypeConverter.TryConvert(raw, variable.Type, out var converted, out var error))
            {
                values[name] = converted;
            }
            else
            {
                typeErrors[name] = error!;
                values[name] = HclValue.Unknown($"var.{name} (invalid type)");
            }
        }

        return values;
    }

    private ValidationResult Evaluate(
        TerraformVariable variable,
        int index,
        Dictionary<string, HclValue> values,
        Dictionary<string, string> typeErrors,
        HclEvaluator evaluator)
    {
        var validation = variable.Validations[index];
        var referenced = new List<string>();
        ExpressionAnalysis.CollectVariableReferences(validation.Condition, referenced);
        var references = referenced.Distinct(StringComparer.Ordinal).ToList();

        ValidationResult Result(ValidationOutcome outcome, string? message = null, string? reason = null)
            => new(variable.Name, index, validation, outcome, message, reason, references);

        if (typeErrors.TryGetValue(variable.Name, out var typeError))
        {
            return Result(ValidationOutcome.Indeterminate, reason: $"The value does not match the declared type: {typeError}");
        }

        var variables = HclValue.FromObject(values);
        var context = new HclEvaluationContext();
        context.SetVariable("var", variables);
        if (_localReferences.TryGetValue(validation, out var localNames) && localNames.Length > 0)
        {
            context.SetVariable("local", _locals.Resolve(localNames, variables, evaluator));
        }

        HclValue condition;
        try
        {
            condition = evaluator.Evaluate(validation.Condition, context);
        }
        catch (HclEvaluationLimitException exception)
        {
            return Result(ValidationOutcome.Indeterminate, reason: exception.Message);
        }
        catch (Exception exception)
        {
            return Result(ValidationOutcome.Indeterminate, reason: $"The condition could not be evaluated: {exception.Message}");
        }

        switch (condition.Type)
        {
            case HclValueType.Unknown:
                return Result(ValidationOutcome.Indeterminate, reason: $"The condition depends on {FindUnknownCause(condition)}, which cannot be evaluated offline.");
            case HclValueType.Bool when condition.BoolValue:
                return Result(ValidationOutcome.Passed);
            case HclValueType.Bool:
                return Result(ValidationOutcome.Failed, message: RenderMessage(validation, context, evaluator));
            case HclValueType.String when condition.StringValue is "true" or "1":
                return Result(ValidationOutcome.Passed);
            case HclValueType.String when condition.StringValue is "false" or "0":
                return Result(ValidationOutcome.Failed, message: RenderMessage(validation, context, evaluator));
            default:
                return Result(ValidationOutcome.Indeterminate, reason: "The condition did not evaluate to a boolean.");
        }
    }

    private static string FindUnknownCause(HclValue unknown)
    {
        var current = unknown;
        while (true)
        {
            HclValue? next = null;
            foreach (var argument in current.UnknownArgs)
            {
                if (argument.Type == HclValueType.Unknown)
                {
                    next = argument;

                    break;
                }
            }

            if (next is null)
            {
                return current.UnknownSource;
            }

            current = next;
        }
    }

    private static string RenderMessage(TerraformValidation validation, HclEvaluationContext context, HclEvaluator evaluator)
    {
        try
        {
            var rendered = evaluator.Evaluate(validation.ErrorMessageExpression, context);

            return (rendered.Type == HclValueType.String ? rendered.StringValue : validation.ErrorMessage).Trim();
        }
        catch (Exception)
        {
            return validation.ErrorMessage.Trim();
        }
    }

    private List<string> FindRequiredNow(
        Dictionary<string, HclValue> values,
        Dictionary<string, string> typeErrors,
        HclEvaluator evaluator)
    {
        var required = new List<string>();
        foreach (var variable in _module.Variables)
        {
            if (variable.IsRequired)
            {
                required.Add(variable.Name);

                continue;
            }

            if (variable.Default is not HclLiteralExpression { Kind: HclLiteralKind.Null }
                || variable.Validations.Count == 0
                || typeErrors.ContainsKey(variable.Name))
            {
                continue;
            }

            var withNull = new Dictionary<string, HclValue>(values, StringComparer.Ordinal)
            {
                [variable.Name] = HclValue.Null,
            };

            for (var index = 0; index < variable.Validations.Count; index++)
            {
                if (Evaluate(variable, index, withNull, typeErrors, evaluator).Outcome == ValidationOutcome.Failed)
                {
                    required.Add(variable.Name);

                    break;
                }
            }
        }

        return required;
    }
}
