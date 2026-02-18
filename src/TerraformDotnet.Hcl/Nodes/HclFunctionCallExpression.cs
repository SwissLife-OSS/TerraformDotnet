namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a function call expression: <c>name(args...)</c>.
/// </summary>
public sealed class HclFunctionCallExpression : HclExpression
{
    /// <summary>Gets or sets the function name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets the function arguments.</summary>
    public List<HclExpression> Arguments { get; } = [];

    /// <summary>Gets or sets whether the final argument uses expansion (<c>...</c>).</summary>
    public bool ExpandFinalArgument { get; set; }

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitFunctionCall(this);

    /// <inheritdoc />
    public override HclNode DeepClone()
    {
        var clone = new HclFunctionCallExpression
        {
            Name = Name,
            ExpandFinalArgument = ExpandFinalArgument,
            Start = Start,
            End = End,
        };

        foreach (var arg in Arguments)
        {
            clone.Arguments.Add((HclExpression)arg.DeepClone());
        }

        return clone;
    }
}
