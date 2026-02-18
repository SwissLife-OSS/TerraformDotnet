namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Identifies the type of a resolved HCL value.
/// </summary>
public enum HclValueType : byte
{
    /// <summary>A string value.</summary>
    String,

    /// <summary>A numeric value (integer or floating-point).</summary>
    Number,

    /// <summary>A boolean value (<c>true</c> or <c>false</c>).</summary>
    Bool,

    /// <summary>A null value.</summary>
    Null,

    /// <summary>An ordered list of values (HCL tuple or list).</summary>
    Tuple,

    /// <summary>A string-keyed map of values (HCL object or map).</summary>
    Object,

    /// <summary>
    /// A value that could not be resolved at evaluation time, such as the result
    /// of a function call or a reference to an undefined variable with a fallback.
    /// </summary>
    Unknown,
}
