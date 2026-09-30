using System.Collections.ObjectModel;
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
/// <para>
/// The constructor analyses the module once (variable defaults, <c>local.*</c> references, the variables
/// each condition reads), so create one validator per module and reuse it: a <c>Validate</c> call only
/// does the work that depends on the supplied values. An instance is immutable and may be shared
/// between threads.
/// </para>
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
    // Everything below that depends only on the module is computed once, so a Validate call only
    // does the work that depends on the supplied values. TerraformVariable is immutable.
    private static readonly HclEvaluationContext EmptyContext = new();

    private readonly ModuleValidatorOptions _options;
    private readonly ModuleLocals _locals;
    private readonly VariablePlan[] _variables;
    private readonly int _validationCount;

    /// <summary>Initializes a validator for a module.</summary>
    /// <param name="module">The module whose variables are validated.</param>
    /// <param name="options">Optional evaluation settings.</param>
    public ModuleValidator(TerraformModule module, ModuleValidatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        _options = options ?? new ModuleValidatorOptions();
        _locals = new ModuleLocals(module);

        var evaluator = CreateEvaluator();
        _variables = new VariablePlan[module.Variables.Count];
        for (var index = 0; index < _variables.Length; index++)
        {
            _variables[index] = CreatePlan(module.Variables[index], evaluator);
            _validationCount += _variables[index].Validations.Length;
        }
    }

    private VariablePlan CreatePlan(TerraformVariable variable, HclEvaluator evaluator)
    {
        var validations = new ValidationPlan[variable.Validations.Count];
        for (var index = 0; index < validations.Length; index++)
        {
            var validation = variable.Validations[index];
            var text = ModuleLocals.TextOf(validation.Condition) + " " + ModuleLocals.TextOf(validation.ErrorMessageExpression);

            var referenced = new List<string>();
            ExpressionAnalysis.CollectVariableReferences(validation.Condition, referenced);

            validations[index] = new ValidationPlan(
                validation,
                _locals.FindReferences(text).Distinct(StringComparer.Ordinal).ToArray(),
                Array.AsReadOnly(referenced.Distinct(StringComparer.Ordinal).ToArray()));
        }

        // Defaults are constant expressions evaluated without context, so the value never changes.
        var defaultValue = variable.Default is null
            ? null
            : EvaluateStandalone(variable.Default, evaluator, variable.Name, "default");

        return new VariablePlan(
            variable,
            validations,
            defaultValue,
            variable.Default is HclLiteralExpression { Kind: HclLiteralKind.Null });
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
            evaluated[name] = EvaluateStandalone(expression, evaluator, name);
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

    private static HclValue EvaluateStandalone(HclExpression expression, HclEvaluator evaluator, string variableName, string? qualifier = null)
    {
        try
        {
            return evaluator.Evaluate(expression, EmptyContext);
        }
        catch (Exception)
        {
            return HclValue.Unknown(qualifier is null ? $"var.{variableName}" : $"var.{variableName} ({qualifier})");
        }
    }

    private ValidationReport ValidateCore(IReadOnlyDictionary<string, HclValue> supplied, HclEvaluator evaluator)
    {
        var typeErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = ResolveValues(supplied, evaluator, typeErrors);
        var run = new EvaluationRun(this, evaluator, values, typeErrors);

        var results = new List<ValidationResult>(_validationCount);
        foreach (var variable in _variables)
        {
            for (var index = 0; index < variable.Validations.Length; index++)
            {
                results.Add(Evaluate(variable, index, run, renderMessage: true));
            }
        }

        var required = FindRequiredNow(run);

        return new ValidationReport(results, values, typeErrors, required);
    }

    private Dictionary<string, HclValue> ResolveValues(
        IReadOnlyDictionary<string, HclValue> supplied,
        HclEvaluator evaluator,
        Dictionary<string, string> typeErrors)
    {
        var values = new Dictionary<string, HclValue>(_variables.Length, StringComparer.Ordinal);
        foreach (var plan in _variables)
        {
            var variable = plan.Variable;
            var name = variable.Name;
            var raw = supplied.TryGetValue(name, out var given)
                ? given
                : plan.DefaultValue ?? HclValue.Unknown($"var.{name} (no value yet)");

            if (raw.Type == HclValueType.Null && !variable.IsNullable)
            {
                if (plan.DefaultValue is null)
                {
                    typeErrors[name] = "null is not allowed because the variable is declared with nullable = false.";
                    values[name] = HclValue.Unknown($"var.{name} (invalid)");

                    continue;
                }

                raw = plan.DefaultValue;
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

    private ValidationResult Evaluate(VariablePlan variable, int index, EvaluationRun run, bool renderMessage)
    {
        var plan = variable.Validations[index];
        var validation = plan.Validation;
        var name = variable.Variable.Name;

        ValidationResult Result(ValidationOutcome outcome, string? message = null, string? reason = null)
            => new(name, index, validation, outcome, message, reason, plan.References);

        if (run.TypeErrors.TryGetValue(name, out var typeError))
        {
            return Result(ValidationOutcome.Indeterminate, reason: $"The value does not match the declared type: {typeError}");
        }

        var context = run.ContextFor(plan);

        HclValue condition;
        try
        {
            condition = run.Evaluator.Evaluate(validation.Condition, context);
        }
        catch (HclEvaluationLimitException exception)
        {
            return Result(ValidationOutcome.Indeterminate, reason: exception.Message);
        }
        catch (Exception exception)
        {
            return Result(ValidationOutcome.Indeterminate, reason: $"The condition could not be evaluated: {exception.Message}");
        }

        // Only failures report a message, and the required-now probe discards the messages anyway.
        string? FailureMessage() => renderMessage ? RenderMessage(validation, context, run.Evaluator) : null;

        switch (condition.Type)
        {
            case HclValueType.Unknown:
                return Result(ValidationOutcome.Indeterminate, reason: $"The condition depends on {FindUnknownCause(condition)}, which cannot be evaluated offline.");
            case HclValueType.Bool when condition.BoolValue:
                return Result(ValidationOutcome.Passed);
            case HclValueType.Bool:
                return Result(ValidationOutcome.Failed, message: FailureMessage());
            case HclValueType.String when condition.StringValue is "true" or "1":
                return Result(ValidationOutcome.Passed);
            case HclValueType.String when condition.StringValue is "false" or "0":
                return Result(ValidationOutcome.Failed, message: FailureMessage());
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

    private List<string> FindRequiredNow(EvaluationRun run)
    {
        var required = new List<string>();
        foreach (var plan in _variables)
        {
            var variable = plan.Variable;
            if (variable.IsRequired)
            {
                required.Add(variable.Name);

                continue;
            }

            if (!plan.HasNullDefault
                || plan.Validations.Length == 0
                || run.TypeErrors.ContainsKey(variable.Name))
            {
                continue;
            }

            if (FailsWhenNull(plan, run))
            {
                required.Add(variable.Name);
            }
        }

        return required;
    }

    /// <summary>Determines whether a validation of the variable fails when the variable is left <c>null</c>.</summary>
    private bool FailsWhenNull(VariablePlan plan, EvaluationRun run)
    {
        var name = plan.Variable.Name;

        // The candidate is set in the dictionary behind the "var" object and restored afterwards, which
        // avoids copying every variable for each candidate. Nothing keeps a reference to the dictionary.
        var original = run.Values[name];
        run.Values[name] = HclValue.Null;
        run.ForgetLocals();
        try
        {
            for (var index = 0; index < plan.Validations.Length; index++)
            {
                if (Evaluate(plan, index, run, renderMessage: false).Outcome == ValidationOutcome.Failed)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            run.Values[name] = original;
            run.ForgetLocals();
        }
    }

    /// <summary>What is known about a validation before any value is supplied.</summary>
    private sealed record ValidationPlan(
        TerraformValidation Validation,
        string[] LocalNames,
        ReadOnlyCollection<string> References);

    /// <summary>What is known about a variable before any value is supplied.</summary>
    private sealed record VariablePlan(
        TerraformVariable Variable,
        ValidationPlan[] Validations,
        HclValue? DefaultValue,
        bool HasNullDefault);

    /// <summary>
    /// The state of one <c>Validate</c> call: the effective values, the <c>var</c> object over them
    /// (built once instead of once per validation) and the locals evaluated for the current values.
    /// </summary>
    private sealed class EvaluationRun
    {
        private readonly ModuleValidator _owner;
        private readonly HclEvaluationContext _context = new();
        private ModuleLocals.Scope? _locals;

        public EvaluationRun(
            ModuleValidator owner,
            HclEvaluator evaluator,
            Dictionary<string, HclValue> values,
            Dictionary<string, string> typeErrors)
        {
            _owner = owner;
            Evaluator = evaluator;
            Values = values;
            TypeErrors = typeErrors;

            // A live view: FailsWhenNull changes one entry temporarily and the view reflects it.
            Variables = HclValue.WrapObject(new ReadOnlyDictionary<string, HclValue>(values));
            _context.SetVariable("var", Variables);
        }

        public HclEvaluator Evaluator { get; }

        public Dictionary<string, HclValue> Values { get; }

        public Dictionary<string, string> TypeErrors { get; }

        public HclValue Variables { get; }

        /// <summary>Gets the context for a validation; only validations that use locals need a scope of their own.</summary>
        public HclEvaluationContext ContextFor(ValidationPlan plan)
        {
            if (plan.LocalNames.Length == 0)
            {
                return _context;
            }

            _locals ??= _owner._locals.CreateScope(Variables, Evaluator);
            var context = _context.CreateChildScope();
            context.SetVariable("local", _locals.Resolve(plan.LocalNames));

            return context;
        }

        /// <summary>Discards the evaluated locals; call after a value behind <see cref="Variables"/> changed.</summary>
        public void ForgetLocals() => _locals = null;
    }
}
