namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a block definition: <c>type label* { body }</c>.
/// </summary>
public sealed class HclBlock : HclNode
{
    /// <inheritdoc />
    public override HclNodeType NodeType => HclNodeType.Block;

    /// <summary>Gets or sets the block type identifier (e.g., "resource", "variable").</summary>
    public required string Type { get; set; }

    /// <summary>Gets the block labels (string or identifier).</summary>
    public List<string> Labels { get; } = [];

    /// <summary>Gets or sets the block body.</summary>
    public HclBody Body { get; set; } = new();

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitBlock(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclBlock
        {
            Type = Type,
            Body = (HclBody)Body.DeepClone(),
            Start = Start,
            End = End,
        };

        foreach (string label in Labels)
        {
            clone.Labels.Add(label);
        }

        foreach (var comment in LeadingComments)
        {
            clone.LeadingComments.Add(comment);
        }

        clone.TrailingComment = TrailingComment;

        return clone;
    }
}
