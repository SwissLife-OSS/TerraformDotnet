namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Classifies the kind of an HCL node.
/// </summary>
public enum HclNodeType : byte
{
    /// <summary>The root file node.</summary>
    File,

    /// <summary>A body containing attributes and blocks.</summary>
    Body,

    /// <summary>A block definition.</summary>
    Block,

    /// <summary>An attribute definition.</summary>
    Attribute,

    /// <summary>An expression value.</summary>
    Expression,
}
