namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a template expression — a string containing interpolations and/or directives.
/// The <see cref="Parts"/> list alternates between literal text and expression segments.
/// </summary>
public sealed class HclTemplateExpression : HclExpression
{
    /// <summary>Gets the raw template content including interpolation/directive markers.</summary>
    public required string RawContent { get; set; }

    /// <summary>Gets the template parts (literal strings and expression segments).</summary>
    public List<HclExpression> Parts { get; } = [];

    /// <summary>Gets or sets whether this is a heredoc template.</summary>
    public bool IsHeredoc { get; set; }

    /// <summary>Gets or sets whether this is an indented heredoc.</summary>
    public bool IsIndented { get; set; }

    /// <summary>Gets or sets the heredoc marker (if applicable).</summary>
    public string? HeredocMarker { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitTemplate(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclTemplateExpression
        {
            RawContent = RawContent,
            IsHeredoc = IsHeredoc,
            IsIndented = IsIndented,
            HeredocMarker = HeredocMarker,
            Start = Start,
            End = End,
        };

        foreach (var part in Parts)
        {
            clone.Parts.Add((HclExpression)part.DeepClone());
        }

        return clone;
    }
}
