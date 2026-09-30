namespace TerraformDotnet.Validation;

/// <summary>
/// The comparison operators supported by <see cref="ComparePredicate"/>.
/// </summary>
public enum CompareOperator : byte
{
    /// <summary><c>==</c></summary>
    Equal,

    /// <summary><c>!=</c></summary>
    NotEqual,

    /// <summary><c>&lt;</c></summary>
    LessThan,

    /// <summary><c>&lt;=</c></summary>
    LessThanOrEqual,

    /// <summary><c>&gt;</c></summary>
    GreaterThan,

    /// <summary><c>&gt;=</c></summary>
    GreaterThanOrEqual,
}
