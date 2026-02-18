namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a tuple (list) expression: <c>[expr, ...]</c>.
/// </summary>
public sealed class HclTupleExpression : HclExpression
{
    /// <summary>Gets the tuple elements.</summary>
    public List<HclExpression> Elements { get; } = [];

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitTuple(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclTupleExpression { Start = Start, End = End };

        foreach (var element in Elements)
        {
            clone.Elements.Add((HclExpression)element.DeepClone());
        }

        return clone;
    }
}
