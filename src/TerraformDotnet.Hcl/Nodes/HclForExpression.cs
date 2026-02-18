namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a for expression (tuple or object variant).
/// Tuple: <c>[for v in list : expr]</c>.
/// Object: <c>{for k, v in map : keyExpr => valueExpr}</c>.
/// </summary>
public sealed class HclForExpression : HclExpression
{
    /// <summary>Gets or sets the first iterator variable name (value or key).</summary>
    public required string KeyVariable { get; set; }

    /// <summary>Gets or sets the second iterator variable name, or null if not specified.</summary>
    public string? ValueVariable { get; set; }

    /// <summary>Gets or sets the collection expression being iterated.</summary>
    public required HclExpression Collection { get; set; }

    /// <summary>Gets or sets the key expression (for object-for) or value expression (for tuple-for).</summary>
    public required HclExpression ValueExpression { get; set; }

    /// <summary>Gets or sets the key expression for object-for (produces the key via <c>=></c>).</summary>
    public HclExpression? KeyExpression { get; set; }

    /// <summary>Gets or sets the optional filter condition (<c>if</c> clause).</summary>
    public HclExpression? Condition { get; set; }

    /// <summary>Gets or sets whether this is an object-for expression.</summary>
    public bool IsObjectFor { get; set; }

    /// <summary>Gets or sets whether grouping is enabled (<c>...</c> after value in object-for).</summary>
    public bool IsGrouped { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitFor(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclForExpression
        {
            KeyVariable = KeyVariable,
            ValueVariable = ValueVariable,
            Collection = (HclExpression)Collection.DeepClone(),
            ValueExpression = (HclExpression)ValueExpression.DeepClone(),
            KeyExpression = KeyExpression is not null ? (HclExpression)KeyExpression.DeepClone() : null,
            Condition = Condition is not null ? (HclExpression)Condition.DeepClone() : null,
            IsObjectFor = IsObjectFor,
            IsGrouped = IsGrouped,
            Start = Start,
            End = End,
        };

        return clone;
    }
}
