namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Indicates the style of an HCL comment.
/// </summary>
public enum HclCommentStyle : byte
{
    /// <summary>Line comment using <c>//</c>.</summary>
    Line,

    /// <summary>Line comment using <c>#</c>.</summary>
    Hash,

    /// <summary>Block comment using <c>/* ... */</c>.</summary>
    Block,
}
