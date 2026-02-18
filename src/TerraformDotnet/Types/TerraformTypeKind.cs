namespace TerraformDotnet.Types;

/// <summary>
/// Describes the kind of a Terraform type constraint.
/// </summary>
public enum TerraformTypeKind : byte
{
    /// <summary>A string value.</summary>
    String,

    /// <summary>A numeric value.</summary>
    Number,

    /// <summary>A boolean value.</summary>
    Bool,

    /// <summary>Any type (unconstrained).</summary>
    Any,

    /// <summary>An ordered collection — list(T).</summary>
    List,

    /// <summary>An unordered unique collection — set(T).</summary>
    Set,

    /// <summary>A string-keyed collection — map(T).</summary>
    Map,

    /// <summary>A structural type with named fields — object({ ... }).</summary>
    Object,

    /// <summary>A positional structural type — tuple([...]).</summary>
    Tuple,
}
