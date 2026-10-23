namespace TerraformDotnet.Validation;

/// <summary>
/// Describes what a <see cref="VariableConstraint"/> applies to.
/// </summary>
public enum ConstraintTarget : byte
{
    /// <summary>The constraint applies to the variable value itself.</summary>
    Value,

    /// <summary>
    /// The constraint applies to every element of a list or set variable, e.g.
    /// <c>alltrue([for v in var.tiers : contains(["a", "b"], v)])</c>.
    /// </summary>
    Elements,
}
