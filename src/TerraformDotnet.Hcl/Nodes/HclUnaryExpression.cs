namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a unary expression: <c>-expr</c> or <c>!expr</c>.
/// </summary>
public sealed class HclUnaryExpression : HclExpression
{
    /// <summary>Gets or sets the operator.</summary>
    public required HclUnaryOperator Operator { get; set; }

    /// <summary>Gets or sets the operand expression.</summary>
    public required HclExpression Operand { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitUnary(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclUnaryExpression
    {
        Operator = Operator,
        Operand = (HclExpression)Operand.DeepClone(),
        Start = Start,
        End = End,
    };
}

/// <summary>
/// Unary operators supported in HCL.
/// </summary>
public enum HclUnaryOperator : byte
{
    /// <summary>Arithmetic negation (<c>-</c>).</summary>
    Negate,

    /// <summary>Logical negation (<c>!</c>).</summary>
    Not,
}
