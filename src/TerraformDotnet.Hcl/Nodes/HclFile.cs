namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents the root node of an HCL file.
/// </summary>
public sealed class HclFile : HclNode
{
    /// <inheritdoc />
    public override HclNodeType NodeType => HclNodeType.File;

    /// <summary>Gets the root body of the file.</summary>
    public HclBody Body { get; set; } = new();

    /// <summary>Gets comments not attached to any specific node (e.g., at end of file).</summary>
    public List<HclComment> DanglingComments { get; } = [];

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitFile(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclFile
        {
            Start = Start,
            End = End,
            Body = (HclBody)Body.DeepClone(),
        };

        foreach (var comment in LeadingComments)
        {
            clone.LeadingComments.Add(comment);
        }

        clone.TrailingComment = TrailingComment;

        foreach (var comment in DanglingComments)
        {
            clone.DanglingComments.Add(comment);
        }

        return clone;
    }

    /// <summary>
    /// Parses the given UTF-8 bytes into an HCL file AST.
    /// </summary>
    /// <param name="hclData">The UTF-8 encoded HCL source.</param>
    /// <returns>The parsed file node.</returns>
    public static HclFile Load(ReadOnlySpan<byte> hclData)
    {
        return HclFileParser.Parse(hclData);
    }
}
