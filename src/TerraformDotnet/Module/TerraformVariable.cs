using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Types;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a <c>variable "name" { ... }</c> block in a Terraform module.
/// <example>
/// <code>
/// // variable "region" {
/// //   type        = string
/// //   description = "The deployment region."
/// // }
/// var v = module.Variables.First(v => v.Name == "region");
/// Console.WriteLine(v.IsRequired); // true (no default)
/// </code>
/// </example>
/// </summary>
public sealed class TerraformVariable
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformVariable"/>.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="type">The parsed type constraint, or <c>null</c> for implicit <c>any</c>.</param>
    /// <param name="description">The variable description.</param>
    /// <param name="defaultValue">The default value expression. <c>null</c> means the variable is required.</param>
    /// <param name="isSensitive">Whether the variable is marked <c>sensitive</c>.</param>
    /// <param name="isNullable">Whether the variable is marked <c>nullable</c>.</param>
    /// <param name="validation">The validation block, if any.</param>
    internal TerraformVariable(
        string name,
        TerraformType? type = null,
        string? description = null,
        HclExpression? defaultValue = null,
        bool isSensitive = false,
        bool isNullable = false,
        TerraformValidation? validation = null)
    {
        Name = name;
        Type = type;
        Description = description;
        Default = defaultValue;
        IsSensitive = isSensitive;
        IsNullable = isNullable;
        Validation = validation;
    }

    /// <summary>Gets the variable name.</summary>
    public string Name { get; }

    /// <summary>Gets the parsed type constraint, or <c>null</c> for implicit <c>any</c>.</summary>
    public TerraformType? Type { get; }

    /// <summary>Gets the variable description.</summary>
    public string? Description { get; }

    /// <summary>Gets the default value expression. <c>null</c> means the variable is required.</summary>
    public HclExpression? Default { get; }

    /// <summary>Gets whether this variable is required (has no default).</summary>
    public bool IsRequired => Default is null;

    /// <summary>Gets whether this variable is optional (has a default).</summary>
    public bool IsOptional => Default is not null;

    /// <summary>Gets whether this variable is marked <c>sensitive</c>.</summary>
    public bool IsSensitive { get; }

    /// <summary>Gets whether this variable is marked <c>nullable</c>.</summary>
    public bool IsNullable { get; }

    /// <summary>Gets the validation block, if any.</summary>
    public TerraformValidation? Validation { get; }
}
