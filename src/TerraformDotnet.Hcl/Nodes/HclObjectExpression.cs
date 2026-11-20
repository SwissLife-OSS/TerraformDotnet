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

    /// <summary>
    /// Adds an element whose key is the literal string <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key text.</param>
    /// <param name="value">The value.</param>
    /// <returns>The added element, for example to attach comments.</returns>
    /// <remarks>
    /// The key is written bare (<c>name = …</c>) when it is an HCL identifier, and quoted
    /// (<c>"my.key" = …</c>) otherwise. <c>for</c>, <c>true</c>, <c>false</c> and <c>null</c> are always
    /// quoted: as bare keys they would open a <c>for</c> expression or be read as a bool or null value.
    /// </remarks>
    public HclObjectElement Add(string key, HclExpression value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        HclExpression keyExpression = IsBareKey(key)
            ? new HclVariableExpression { Name = key }
            : HclLiteralExpression.String(key);

        var element = new HclObjectElement { Key = keyExpression, Value = value };
        Elements.Add(element);

        return element;
    }

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

    // Same ASCII identifier rules as the reader: a letter or '_', then letters, digits, '_' or '-'.
    private static bool IsBareKey(string key)
    {
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }

        foreach (char c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        return key is not ("for" or "true" or "false" or "null");
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
