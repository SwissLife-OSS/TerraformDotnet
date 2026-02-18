namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a variable reference expression (e.g., <c>var</c>, <c>local</c>).
/// </summary>
public sealed class HclVariableExpression : HclExpression
{
    /// <summary>Gets or sets the variable name.</summary>
    public required string Name { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitVariable(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclVariableExpression
    {
        Name = Name,
        Start = Start,
        End = End,
    };
}
