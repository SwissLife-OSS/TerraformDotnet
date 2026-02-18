namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a body in HCL, which is a collection of attributes and blocks.
/// The body is the top-level structure within a file or block.
/// </summary>
public sealed class HclBody : HclNode
{
    /// <inheritdoc />
    public override HclNodeType NodeType => HclNodeType.Body;

    /// <summary>Gets the attributes defined in this body.</summary>
    public List<HclAttribute> Attributes { get; } = [];

    /// <summary>Gets the blocks defined in this body.</summary>
    public List<HclBlock> Blocks { get; } = [];

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitBody(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclBody { Start = Start, End = End };
        foreach (var comment in LeadingComments)
        {
            clone.LeadingComments.Add(comment);
        }

        clone.TrailingComment = TrailingComment;

        foreach (var attr in Attributes)
        {
            clone.Attributes.Add((HclAttribute)attr.DeepClone());
        }

        foreach (var block in Blocks)
        {
            clone.Blocks.Add((HclBlock)block.DeepClone());
        }

        return clone;
    }
}
