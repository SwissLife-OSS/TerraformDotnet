namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a conditional (ternary) expression: <c>condition ? trueExpr : falseExpr</c>.
/// </summary>
public sealed class HclConditionalExpression : HclExpression
{
    /// <summary>Gets or sets the condition expression.</summary>
    public required HclExpression Condition { get; set; }

    /// <summary>Gets or sets the expression evaluated when the condition is true.</summary>
    public required HclExpression TrueResult { get; set; }

    /// <summary>Gets or sets the expression evaluated when the condition is false.</summary>
    public required HclExpression FalseResult { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitConditional(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclConditionalExpression
    {
        Condition = (HclExpression)Condition.DeepClone(),
        TrueResult = (HclExpression)TrueResult.DeepClone(),
        FalseResult = (HclExpression)FalseResult.DeepClone(),
        Start = Start,
        End = End,
    };
}
