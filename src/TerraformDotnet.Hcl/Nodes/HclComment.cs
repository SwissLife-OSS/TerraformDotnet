using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a comment in HCL source code.
/// </summary>
public sealed class HclComment
{
    /// <summary>Gets or sets the comment text (excluding the comment markers).</summary>
    public required string Text { get; set; }

    /// <summary>Gets or sets the comment style.</summary>
    public required HclCommentStyle Style { get; set; }

    /// <summary>Gets or sets the start position of the comment.</summary>
    public Mark Start { get; set; }

    /// <summary>Gets or sets the end position of the comment.</summary>
    public Mark End { get; set; }
}
