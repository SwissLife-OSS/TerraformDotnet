namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a splat expression: <c>expr.*.attr</c> or <c>expr[*].attr</c>.
/// </summary>
public sealed class HclSplatExpression : HclExpression
{
    /// <summary>Gets or sets the source expression.</summary>
    public required HclExpression Source { get; set; }

    /// <summary>Gets the traversal chain applied to each element.</summary>
    public List<HclExpression> Traversal { get; } = [];

    /// <summary>Gets or sets whether this is a full splat (<c>[*]</c>) or attribute splat (<c>.*</c>).</summary>
    public bool IsFullSplat { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitSplat(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclSplatExpression
        {
            Source = (HclExpression)Source.DeepClone(),
            IsFullSplat = IsFullSplat,
            Start = Start,
            End = End,
        };

        foreach (var t in Traversal)
        {
            clone.Traversal.Add((HclExpression)t.DeepClone());
        }

        return clone;
    }
}
