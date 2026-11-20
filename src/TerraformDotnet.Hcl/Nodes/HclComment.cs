using TerraformDotnet.Hcl.Common;

namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a comment in HCL source code.
/// </summary>
public sealed class HclComment
{
    /// <summary>Gets or sets the comment text (excluding the comment markers).</summary>
    public required string Text { get; set; }

    /// <summary>Gets or sets the comment style.</summary>
    public required HclCommentStyle Style { get; set; }

    /// <summary>Gets or sets the start position of the comment.</summary>
    public Mark Start { get; set; }

    /// <summary>Gets or sets the end position of the comment.</summary>
    public Mark End { get; set; }

    /// <summary>Creates a <c># text</c> comment.</summary>
    /// <param name="text">The comment text, without the marker. A space is put between marker and text.</param>
    /// <returns>The comment.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains a line break.</exception>
    public static HclComment Hash(string text) => SingleLine(text, HclCommentStyle.Hash);

    /// <summary>Creates a <c>// text</c> comment.</summary>
    /// <param name="text">The comment text, without the marker. A space is put between marker and text.</param>
    /// <returns>The comment.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains a line break.</exception>
    public static HclComment Line(string text) => SingleLine(text, HclCommentStyle.Line);

    /// <summary>Creates a <c>/* text */</c> comment, which may span several lines.</summary>
    /// <param name="text">The comment text, without the markers. A space is put on each side.</param>
    /// <returns>The comment.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains <c>*/</c>.</exception>
    public static HclComment Block(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Contains("*/", StringComparison.Ordinal))
        {
            throw new ArgumentException("A block comment cannot contain '*/'.", nameof(text));
        }

        return new HclComment { Text = $" {text} ", Style = HclCommentStyle.Block };
    }

    private static HclComment SingleLine(string text, HclCommentStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException(
                "A line comment cannot contain a line break; use one comment per line or a block comment.",
                nameof(text));
        }

        return new HclComment { Text = text.Length == 0 ? string.Empty : $" {text}", Style = style };
    }
}
