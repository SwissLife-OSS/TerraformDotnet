namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a template wrap expression — a single-interpolation template eligible
/// for unwrapping (e.g., <c>"${var.value}"</c> evaluates to the inner value's type
/// rather than always producing a string).
/// </summary>
public sealed class HclTemplateWrapExpression : HclExpression
{
    /// <summary>Gets or sets the wrapped expression.</summary>
    public required HclExpression Wrapped { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitTemplateWrap(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclTemplateWrapExpression
    {
        Wrapped = (HclExpression)Wrapped.DeepClone(),
        Start = Start,
        End = End,
    };
}
