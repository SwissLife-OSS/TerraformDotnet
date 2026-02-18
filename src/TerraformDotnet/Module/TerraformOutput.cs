using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents an <c>output "name" { ... }</c> block in a Terraform module.
/// <example>
/// <code>
/// // output "instance_id" {
/// //   value       = cloud_instance.main.id
/// //   description = "The ID of the compute instance."
/// // }
/// </code>
/// </example>
/// </summary>
public sealed class TerraformOutput
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformOutput"/>.
    /// </summary>
    /// <param name="name">The output name.</param>
    /// <param name="value">The value expression.</param>
    /// <param name="description">The output description.</param>
    /// <param name="isSensitive">Whether the output is marked <c>sensitive</c>.</param>
    /// <param name="dependsOn">The explicit dependency list.</param>
    internal TerraformOutput(
        string name,
        HclExpression value,
        string? description = null,
        bool isSensitive = false,
        IReadOnlyList<string>? dependsOn = null)
    {
        Name = name;
        Value = value;
        Description = description;
        IsSensitive = isSensitive;
        DependsOn = dependsOn;
    }

    /// <summary>Gets the output name.</summary>
    public string Name { get; }

    /// <summary>Gets the value expression.</summary>
    public HclExpression Value { get; }

    /// <summary>Gets the output description.</summary>
    public string? Description { get; }

    /// <summary>Gets whether this output is marked <c>sensitive</c>.</summary>
    public bool IsSensitive { get; }

    /// <summary>Gets the explicit dependency list.</summary>
    public IReadOnlyList<string>? DependsOn { get; }
}
