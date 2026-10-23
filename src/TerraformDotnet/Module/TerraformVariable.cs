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
    /// <param name="isNullable">Whether the variable accepts <c>null</c>; Terraform defaults to <c>true</c>.</param>
    /// <param name="validations">The validation blocks, in declaration order.</param>
    internal TerraformVariable(
        string name,
        TerraformType? type,
        string? description,
        HclExpression? defaultValue,
        bool isSensitive,
        bool isNullable,
        IReadOnlyList<TerraformValidation> validations)
    {
        Name = name;
        Type = type;
        Description = description;
        Default = defaultValue;
        IsSensitive = isSensitive;
        IsNullable = isNullable;
        Validations = validations;
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

    /// <summary>
    /// Returns <c>true</c> when the variable has a string default whose value equals <c>sentinel</c>.
    /// This identifies variables that use a well-known placeholder (e.g. <c>"inject-at-runtime"</c>)
    /// to signal that the real value is supplied externally (environment variables, CI/CD pipelines, etc.).
    /// <example>
    /// <code>
    /// // variable "service_token" {
    /// //   type    = string
    /// //   default = "inject-at-runtime"
    /// // }
    /// var v = module.Variables.First(v => v.Name == "service_token");
    /// Console.WriteLine(v.HasSentinelDefault("inject-at-runtime")); // true
    /// Console.WriteLine(v.IsOptional); // true (has a default)
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="sentinel">The sentinel string to match against (ordinal comparison).</param>
    public bool HasSentinelDefault(string sentinel) =>
        Default is HclLiteralExpression { Kind: HclLiteralKind.String, Value: { } value }
        && string.Equals(value, sentinel, StringComparison.Ordinal);

    /// <summary>Gets whether this variable is marked <c>sensitive</c>.</summary>
    public bool IsSensitive { get; }

    /// <summary>
    /// Gets whether the variable accepts <c>null</c>. Terraform's default is <c>true</c>, so this is
    /// only <c>false</c> when the declaration says <c>nullable = false</c>.
    /// </summary>
    public bool IsNullable { get; }

    /// <summary>
    /// Gets all <c>validation</c> blocks in declaration order. Terraform allows several per variable
    /// and every one of them must hold.
    /// </summary>
    public IReadOnlyList<TerraformValidation> Validations { get; }
}
