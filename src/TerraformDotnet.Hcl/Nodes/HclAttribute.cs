namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents an attribute definition: <c>name = expression</c>.
/// </summary>
public sealed class HclAttribute : HclNode
{
    /// <inheritdoc />
    public override HclNodeType NodeType => HclNodeType.Attribute;

    /// <summary>Gets or sets the attribute name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the attribute value expression.</summary>
    public required HclExpression Value { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitAttribute(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclAttribute
        {
            Name = Name,
            Value = (HclExpression)Value.DeepClone(),
            Start = Start,
            End = End,
        };

        clone.HasLeadingBlankLine = HasLeadingBlankLine;

        foreach (var comment in LeadingComments)
        {
            clone.LeadingComments.Add(comment);
        }

        clone.TrailingComment = TrailingComment;

        return clone;
    }
}
