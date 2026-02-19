using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Abstract base class for all HCL AST nodes.
/// </summary>
public abstract class HclNode
{
    /// <summary>Gets or sets the start position of this node in the source.</summary>
    public Mark Start { get; set; }

    /// <summary>Gets or sets the end position of this node in the source.</summary>
    public Mark End { get; set; }

    /// <summary>Gets the kind of this node.</summary>
    public abstract HclNodeType NodeType { get; }

    /// <summary>Gets the comments appearing before this node.</summary>
    public List<HclComment> LeadingComments { get; } = [];

    /// <summary>Gets or sets the comment appearing on the same line after this node.</summary>
    public HclComment? TrailingComment { get; set; }

    /// <summary>
    /// Indicates that a blank line appeared before this node in the source.
    /// Used by the emitter for blank-line preservation and per-group <c>=</c> alignment.
    /// </summary>
    public bool HasLeadingBlankLine { get; set; }

    /// <summary>
    /// Accepts a visitor for tree traversal.
    /// </summary>
    /// <param name="visitor">The visitor to accept.</param>
    public abstract void Accept(IHclVisitor visitor);

    /// <summary>
    /// Creates a deep clone of this node.
    /// </summary>
    /// <returns>An independent copy of the entire subtree.</returns>
    public abstract HclNode DeepClone();
}
