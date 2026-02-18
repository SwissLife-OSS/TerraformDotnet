namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Binary operators supported in HCL expressions.
/// </summary>
public enum HclBinaryOperator : byte
{
    /// <summary>Addition (<c>+</c>).</summary>
    Add,

    /// <summary>Subtraction (<c>-</c>).</summary>
    Subtract,

    /// <summary>Multiplication (<c>*</c>).</summary>
    Multiply,

    /// <summary>Division (<c>/</c>).</summary>
    Divide,

    /// <summary>Modulo (<c>%</c>).</summary>
    Modulo,

    /// <summary>Equality (<c>==</c>).</summary>
    Equal,

    /// <summary>Inequality (<c>!=</c>).</summary>
    NotEqual,

    /// <summary>Less than (<c>&lt;</c>).</summary>
    LessThan,

    /// <summary>Greater than (<c>&gt;</c>).</summary>
    GreaterThan,

    /// <summary>Less than or equal (<c>&lt;=</c>).</summary>
    LessEqual,

    /// <summary>Greater than or equal (<c>&gt;=</c>).</summary>
    GreaterEqual,

    /// <summary>Logical AND (<c>&amp;&amp;</c>).</summary>
    And,

    /// <summary>Logical OR (<c>||</c>).</summary>
    Or,
}
