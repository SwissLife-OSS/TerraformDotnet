namespace TerraformDotnet.Types;

/// <summary>
/// A collection Terraform type: <c>list(T)</c>, <c>set(T)</c>, or <c>map(T)</c>.
/// <example>
/// <code>
/// var listOfString = TerraformType.List(TerraformType.String);
/// var mapOfNumber = TerraformType.Map(TerraformType.Number);
/// </code>
/// </example>
/// </summary>
public sealed class TerraformCollectionType : TerraformType
{
    internal TerraformCollectionType(TerraformTypeKind kind, TerraformType element)
    {
        Kind = kind;
        Element = element;
    }

    /// <inheritdoc />
    public override TerraformTypeKind Kind { get; }

    /// <summary>Gets the element type of this collection.</summary>
    public TerraformType Element { get; }

    /// <inheritdoc />
    public override string ToHcl()
    {
        var prefix = Kind switch
        {
            TerraformTypeKind.List => "list",
            TerraformTypeKind.Set => "set",
            TerraformTypeKind.Map => "map",
            _ => throw new InvalidOperationException($"Unexpected collection kind: {Kind}"),
        };

        return $"{prefix}({Element.ToHcl()})";
    }

    /// <inheritdoc />
    public override bool Equals(TerraformType? other) =>
        other is TerraformCollectionType c && c.Kind == Kind && c.Element.Equals(Element);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Element);
}
