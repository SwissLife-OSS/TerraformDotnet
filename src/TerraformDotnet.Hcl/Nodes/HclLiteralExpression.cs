namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a literal value: string, number, boolean, or null.
/// </summary>
public sealed class HclLiteralExpression : HclExpression
{
    /// <summary>Gets or sets the literal value as a string representation.</summary>
    public required string? Value { get; set; }

    /// <summary>Gets or sets the literal kind.</summary>
    public required HclLiteralKind Kind { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitLiteral(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclLiteralExpression
    {
        Value = Value,
        Kind = Kind,
        Start = Start,
        End = End,
    };
}

/// <summary>
/// Classifies the kind of a literal expression.
/// </summary>
public enum HclLiteralKind : byte
{
    /// <summary>A string value.</summary>
    String,

    /// <summary>A numeric value (integer or float).</summary>
    Number,

    /// <summary>A boolean value.</summary>
    Bool,

    /// <summary>A null value.</summary>
    Null,
}
