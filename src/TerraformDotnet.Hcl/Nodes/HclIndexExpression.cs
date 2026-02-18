namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents an index expression: <c>expr[index]</c>.
/// </summary>
public sealed class HclIndexExpression : HclExpression
{
    /// <summary>Gets or sets the collection expression being indexed.</summary>
    public required HclExpression Collection { get; set; }

    /// <summary>Gets or sets the index expression.</summary>
    public required HclExpression Index { get; set; }

    /// <summary>Gets or sets whether this is a legacy index (e.g., <c>list.0</c>).</summary>
    public bool IsLegacy { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitIndex(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclIndexExpression
    {
        Collection = (HclExpression)Collection.DeepClone(),
        Index = (HclExpression)Index.DeepClone(),
        IsLegacy = IsLegacy,
        Start = Start,
        End = End,
    };
}
