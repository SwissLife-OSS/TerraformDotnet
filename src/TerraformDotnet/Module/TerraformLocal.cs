using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a single entry in a <c>locals { ... }</c> block.
/// <example>
/// <code>
/// // locals {
/// //   instance_name = "${var.project_name}-${var.region}"
/// // }
/// </code>
/// </example>
/// </summary>
public sealed class TerraformLocal
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformLocal"/>.
    /// </summary>
    /// <param name="name">The local value name.</param>
    /// <param name="value">The local value expression.</param>
    internal TerraformLocal(string name, HclExpression value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Gets the local value name.</summary>
    public string Name { get; }

    /// <summary>Gets the local value expression.</summary>
    public HclExpression Value { get; }
}
