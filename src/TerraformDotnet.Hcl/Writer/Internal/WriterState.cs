namespace TerraformDotnet.Hcl.Writer.Internal;

/// <summary>
/// Tracks the current state of the <see cref="Utf8HclWriter"/>.
/// </summary>
internal enum WriterState
{
    /// <summary>No output has been written yet.</summary>
    Initial,

    /// <summary>Writing body-level content (attributes and blocks).</summary>
    InBody,

    /// <summary>Writing an attribute value expression.</summary>
    InAttribute,

    /// <summary>Writing elements inside a collection (tuple or object).</summary>
    InCollection,

    /// <summary>Writing has been finalized.</summary>
    Finished,
}
