namespace TerraformDotnet.Hcl.Common;

/// <summary>
/// Specifies the quoting style of a string literal in HCL.
/// </summary>
public enum StringLiteralStyle : byte
{
    /// <summary>A double-quoted string: <c>"hello"</c>.</summary>
    Quoted = 0,

    /// <summary>A heredoc string: <c>&lt;&lt;EOF ... EOF</c>.</summary>
    Heredoc = 1,

    /// <summary>An indented heredoc string: <c>&lt;&lt;-EOF ... EOF</c>.</summary>
    IndentedHeredoc = 2,
}
