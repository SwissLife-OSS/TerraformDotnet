namespace TerraformDotnet.Types;

/// <summary>
/// A Terraform <c>tuple([type, ...])</c> type constraint.
/// <example>
/// <code>
/// var type = TerraformType.Tuple([TerraformType.String, TerraformType.Number]);
/// // type.ToHcl() == "tuple([string, number])"
/// </code>
/// </example>
/// </summary>
public sealed class TerraformTupleType : TerraformType
{
    internal TerraformTupleType(IReadOnlyList<TerraformType> elements)
    {
        Elements = elements;
    }

    /// <inheritdoc />
    public override TerraformTypeKind Kind => TerraformTypeKind.Tuple;

    /// <summary>Gets the positional element types of this tuple.</summary>
    public IReadOnlyList<TerraformType> Elements { get; }

    /// <inheritdoc />
    public override string ToHcl()
    {
        var parts = Elements.Select(e => e.ToHcl());

        return $"tuple([{string.Join(", ", parts)}])";
    }

    /// <inheritdoc />
    public override bool Equals(TerraformType? other)
    {
        if (other is not TerraformTupleType t || t.Elements.Count != Elements.Count)
        {
            return false;
        }

        for (var i = 0; i < Elements.Count; i++)
        {
            if (!Elements[i].Equals(t.Elements[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);

        foreach (var element in Elements)
        {
            hash.Add(element);
        }

        return hash.ToHashCode();
    }
}
