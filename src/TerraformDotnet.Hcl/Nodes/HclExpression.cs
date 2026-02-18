namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Abstract base class for all HCL expression nodes.
/// </summary>
public abstract class HclExpression : HclNode
{
    /// <inheritdoc />
    public override HclNodeType NodeType => HclNodeType.Expression;
}
