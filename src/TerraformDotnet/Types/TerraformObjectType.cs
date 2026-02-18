namespace TerraformDotnet.Types;

/// <summary>
/// A Terraform <c>object({ field = type, ... })</c> type constraint.
/// <example>
/// <code>
/// var type = TerraformType.Object([
///     new TerraformObjectField("name", TerraformType.String),
///     new TerraformObjectField("enabled", TerraformType.Bool, isOptional: true),
/// ]);
/// </code>
/// </example>
/// </summary>
public sealed class TerraformObjectType : TerraformType
{
    internal TerraformObjectType(IReadOnlyList<TerraformObjectField> fields)
    {
        Fields = fields;
    }

    /// <inheritdoc />
    public override TerraformTypeKind Kind => TerraformTypeKind.Object;

    /// <summary>Gets the fields of this object type.</summary>
    public IReadOnlyList<TerraformObjectField> Fields { get; }

    /// <inheritdoc />
    public override string ToHcl()
    {
        if (Fields.Count == 0)
        {
            return "object({})";
        }

        var parts = new List<string>(Fields.Count);

        foreach (var field in Fields)
        {
            var typeHcl = field.IsOptional
                ? field.Default is not null
                    ? $"optional({field.Type.ToHcl()}, {FormatDefault(field.Default)})"
                    : $"optional({field.Type.ToHcl()})"
                : field.Type.ToHcl();

            parts.Add($"{field.Name} = {typeHcl}");
        }

        return $"object({{ {string.Join(", ", parts)} }})";
    }

    /// <inheritdoc />
    public override bool Equals(TerraformType? other)
    {
        if (other is not TerraformObjectType o || o.Fields.Count != Fields.Count)
        {
            return false;
        }

        for (var i = 0; i < Fields.Count; i++)
        {
            if (!Fields[i].Equals(o.Fields[i]))
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

        foreach (var field in Fields)
        {
            hash.Add(field);
        }

        return hash.ToHashCode();
    }

    private static string FormatDefault(Hcl.Nodes.HclExpression expression) => expression switch
    {
        Hcl.Nodes.HclLiteralExpression lit when lit.Kind == Hcl.Nodes.HclLiteralKind.String =>
            $"\"{lit.Value}\"",
        Hcl.Nodes.HclLiteralExpression lit => lit.Value ?? "null",
        Hcl.Nodes.HclTupleExpression { Elements.Count: 0 } => "[]",
        Hcl.Nodes.HclObjectExpression { Elements.Count: 0 } => "{}",
        _ => expression.ToString() ?? "null",
    };
}
