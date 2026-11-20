namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a tuple (list) expression: <c>[expr, ...]</c>.
/// </summary>
public sealed class HclTupleExpression : HclExpression
{
    /// <summary>Gets the tuple elements.</summary>
    public List<HclExpression> Elements { get; } = [];

    /// <summary>Gets the comments on their own lines before the closing <c>]</c>.</summary>
    public List<HclComment> DanglingComments { get; } = [];

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitTuple(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclTupleExpression { Start = Start, End = End };

        foreach (var element in Elements)
        {
            var elementClone = (HclExpression)element.DeepClone();
            element.CopyCommentsTo(elementClone);
            clone.Elements.Add(elementClone);
        }

        clone.DanglingComments.AddRange(DanglingComments);

        return clone;
    }
}
