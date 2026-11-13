namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents an object expression: <c>{ key = value, ... }</c>.
/// </summary>
public sealed class HclObjectExpression : HclExpression
{
    /// <summary>Gets the key-value pairs.</summary>
    public List<HclObjectElement> Elements { get; } = [];

    /// <summary>Gets the comments on their own lines before the closing <c>}</c>.</summary>
    public List<HclComment> DanglingComments { get; } = [];

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitObject(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclObjectExpression { Start = Start, End = End };

        foreach (var element in Elements)
        {
            var elementClone = new HclObjectElement
            {
                Key = (HclExpression)element.Key.DeepClone(),
                Value = (HclExpression)element.Value.DeepClone(),
                ForceKey = element.ForceKey,
                UsesColon = element.UsesColon,
                TrailingComment = element.TrailingComment,
            };
            elementClone.LeadingComments.AddRange(element.LeadingComments);
            clone.Elements.Add(elementClone);
        }

        clone.DanglingComments.AddRange(DanglingComments);

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

    /// <summary>Gets the comments on their own lines before this element.</summary>
    public List<HclComment> LeadingComments { get; } = [];

    /// <summary>Gets or sets the comment on the line where this element ends.</summary>
    public HclComment? TrailingComment { get; set; }
}
