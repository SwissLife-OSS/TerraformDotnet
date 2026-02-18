namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents an attribute access expression: <c>expr.name</c>.
/// </summary>
public sealed class HclAttributeAccessExpression : HclExpression
{
    /// <summary>Gets or sets the source expression.</summary>
    public required HclExpression Source { get; set; }

    /// <summary>Gets or sets the accessed attribute name.</summary>
    public required string Name { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitAttributeAccess(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclAttributeAccessExpression
    {
        Source = (HclExpression)Source.DeepClone(),
        Name = Name,
        Start = Start,
        End = End,
    };
}
