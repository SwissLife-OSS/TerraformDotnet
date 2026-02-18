using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Types;

/// <summary>
/// Represents a single field in a Terraform <c>object({ ... })</c> type constraint.
/// A field may be optional (wrapped in <c>optional(type)</c> or <c>optional(type, default)</c>).
/// <example>
/// <code>
/// var required = new TerraformObjectField("name", TerraformType.String);
/// var optionalWithDefault = new TerraformObjectField("enabled", TerraformType.Bool, isOptional: true, defaultExpression);
/// </code>
/// </example>
/// </summary>
public sealed class TerraformObjectField : IEquatable<TerraformObjectField>
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformObjectField"/>.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <param name="type">The field type.</param>
    /// <param name="isOptional">Whether the field is wrapped in <c>optional()</c>.</param>
    /// <param name="defaultValue">The default value expression, if specified via <c>optional(type, default)</c>.</param>
    public TerraformObjectField(
        string name,
        TerraformType type,
        bool isOptional = false,
        HclExpression? defaultValue = null)
    {
        Name = name;
        Type = type;
        IsOptional = isOptional;
        Default = defaultValue;
    }

    /// <summary>Gets the field name.</summary>
    public string Name { get; }

    /// <summary>Gets the field type (the inner type, not <c>optional()</c> wrapper).</summary>
    public TerraformType Type { get; }

    /// <summary>Gets whether this field is optional.</summary>
    public bool IsOptional { get; }

    /// <summary>Gets the default value expression from <c>optional(type, default)</c>, if any.</summary>
    public HclExpression? Default { get; }

    /// <inheritdoc />
    public bool Equals(TerraformObjectField? other)
    {
        if (other is null)
        {
            return false;
        }

        return Name == other.Name
            && Type.Equals(other.Type)
            && IsOptional == other.IsOptional;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerraformObjectField other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Type, IsOptional);
}
