namespace TerraformDotnet.Hcl.Writer;

/// <summary>
/// Options for configuring a <see cref="Utf8HclWriter"/>.
/// </summary>
/// <remarks>
/// HCL formatting is canonical (<c>terraform fmt</c>), so most options
/// are fixed. Only behaviour toggles are exposed.
/// </remarks>
public sealed class HclWriterOptions
{
    /// <summary>
    /// Gets the default writer options.
    /// </summary>
    public static HclWriterOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the newline style. Default is LF (<c>\n</c>),
    /// which matches <c>terraform fmt</c> canonical output.
    /// </summary>
    public NewLineStyle NewLineStyle { get; init; } = NewLineStyle.Lf;

    /// <summary>
    /// Gets or sets whether comments should be preserved in the output.
    /// Default is <c>true</c>.
    /// </summary>
    public bool PreserveComments { get; init; } = true;
}

/// <summary>
/// Specifies the newline character(s) used in written output.
/// </summary>
public enum NewLineStyle : byte
{
    /// <summary>Line feed (<c>\n</c>). Canonical for <c>terraform fmt</c>.</summary>
    Lf,

    /// <summary>Carriage return + line feed (<c>\r\n</c>).</summary>
    CrLf,
}
