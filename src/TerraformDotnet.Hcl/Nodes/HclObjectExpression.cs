namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents an object expression: <c>{ key = value, ... }</c>.
/// </summary>
public sealed class HclObjectExpression : HclExpression
{
    /// <summary>Gets the key-value pairs.</summary>
    public List<HclObjectElement> Elements { get; } = [];

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitObject(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclObjectExpression { Start = Start, End = End };

        foreach (var element in Elements)
        {
            clone.Elements.Add(new HclObjectElement
            {
                Key = (HclExpression)element.Key.DeepClone(),
                Value = (HclExpression)element.Value.DeepClone(),
                ForceKey = element.ForceKey,
                UsesColon = element.UsesColon,
            });
        }

        return clone;
    }
}

/// <summary>
/// A single key-value pair within an <see cref="HclObjectExpression"/>.
/// </summary>
public sealed class HclObjectElement
{
    /// <summary>Gets or sets the key expression.</summary>
    public required HclExpression Key { get; set; }

    /// <summary>Gets or sets the value expression.</summary>
    public required HclExpression Value { get; set; }

    /// <summary>
    /// Gets or sets whether the key is a forced expression (wrapped in parentheses).
    /// </summary>
    public bool ForceKey { get; set; }

    /// <summary>Gets or sets whether the separator is <c>:</c> instead of <c>=</c>.</summary>
    public bool UsesColon { get; set; }
}
