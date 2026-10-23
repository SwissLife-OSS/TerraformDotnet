using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Validation;

/// <summary>
/// Turns the <c>validation</c> blocks of a Terraform variable into machine-readable
/// <see cref="VariableConstraint"/>s, e.g. to render a dropdown for
/// <c>contains(["silver", "gold"], var.sla)</c> or to make a variable required only for some values of
/// another one.
/// <para>
/// Extraction is purely structural: conditions are never evaluated, only pattern-matched. Anything that is
/// not recognised is reported as an <see cref="OpaqueConstraint"/> rather than guessed, so a UI built on
/// the result can only ever <em>narrow</em> what the user may enter, never widen it beyond Terraform's own rules.
/// </para>
/// <example>
/// <code>
/// var module = TerraformModule.LoadFromContent(bytes);
/// var sla = module.Variables.First(v => v.Name == "sla");
///
/// // condition = contains(["silver", "gold"], var.sla)
/// var constraint = Assert.IsType&lt;AllowedValuesConstraint&gt;(
///     Assert.Single(ConstraintExtractor.Extract(sla, module)));
/// Console.WriteLine(string.Join(", ", constraint.Values.Select(v => v.Value))); // silver, gold
/// </code>
/// </example>
/// </summary>
public static class ConstraintExtractor
{
    /// <summary>
    /// Extracts the constraints of every <c>validation</c> block of <paramref name="variable"/>.
    /// Constraints of different blocks are never merged, because each block has its own error message.
    /// </summary>
    /// <param name="variable">The variable to analyse.</param>
    /// <param name="module">
    /// The module the variable belongs to. When supplied, <c>local.*</c> values that hold literal lists or
    /// objects can be resolved (e.g. <c>contains(local.slas, var.sla)</c>).
    /// </param>
    /// <returns>The constraints in declaration order. Empty when the variable has no validation.</returns>
    public static IReadOnlyList<VariableConstraint> Extract(TerraformVariable variable, TerraformModule? module = null)
    {
        ArgumentNullException.ThrowIfNull(variable);

        return Extract(variable, BuildLocals(module));
    }

    /// <summary>
    /// Extracts the constraints of every variable in <paramref name="module"/>.
    /// </summary>
    /// <param name="module">The module to analyse.</param>
    /// <returns>The constraints per variable name; variables without validation map to an empty list.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<VariableConstraint>> ExtractAll(TerraformModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var locals = BuildLocals(module);
        var result = new Dictionary<string, IReadOnlyList<VariableConstraint>>(module.Variables.Count);

        foreach (var variable in module.Variables)
        {
            result[variable.Name] = Extract(variable, locals);
        }

        return result;
    }

    /// <summary>
    /// Gets the names of the <em>other</em> variables that the validation conditions of
    /// <paramref name="variable"/> refer to, in first-seen order. These are the variables whose value can
    /// change the constraints of <paramref name="variable"/>, so a UI knows when to re-evaluate.
    /// <example>
    /// <code>
    /// // condition = var.sla != "gold" || var.backup_vault_id != null
    /// ConstraintExtractor.GetReferencedVariables(backupVaultId); // ["sla"]
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="variable">The variable to analyse.</param>
    /// <returns>The distinct names of the referenced other variables.</returns>
    public static IReadOnlyList<string> GetReferencedVariables(TerraformVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        var names = new List<string>();

        foreach (var validation in variable.Validations)
        {
            ExpressionAnalysis.CollectVariableReferences(validation.Condition, names);
        }

        return [.. names.Where(name => name != variable.Name).Distinct()];
    }

    private static List<VariableConstraint> Extract(
        TerraformVariable variable,
        IReadOnlyDictionary<string, HclExpression> locals)
    {
        var constraints = new List<VariableConstraint>();

        foreach (var validation in variable.Validations)
        {
            var extraction = new ConstraintExtraction(variable.Name, locals, validation.ErrorMessage);
            constraints.AddRange(extraction.Extract(validation.Condition));
        }

        return constraints;
    }

    private static Dictionary<string, HclExpression> BuildLocals(TerraformModule? module)
    {
        var locals = new Dictionary<string, HclExpression>(StringComparer.Ordinal);

        if (module is not null)
        {
            foreach (var local in module.Locals)
            {
                locals.TryAdd(local.Name, local.Value);
            }
        }

        return locals;
    }
}
