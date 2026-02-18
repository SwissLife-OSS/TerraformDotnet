namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a binary expression: <c>left op right</c>.
/// </summary>
public sealed class HclBinaryExpression : HclExpression
{
    /// <summary>Gets or sets the left-hand operand.</summary>
    public required HclExpression Left { get; set; }

    /// <summary>Gets or sets the binary operator.</summary>
    public required HclBinaryOperator Operator { get; set; }

    /// <summary>Gets or sets the right-hand operand.</summary>
    public required HclExpression Right { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitBinary(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclBinaryExpression
    {
        Left = (HclExpression)Left.DeepClone(),
        Operator = Operator,
        Right = (HclExpression)Right.DeepClone(),
        Start = Start,
        End = End,
    };
}
