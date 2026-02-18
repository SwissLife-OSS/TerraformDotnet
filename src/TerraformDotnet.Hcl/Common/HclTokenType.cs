namespace TerraformDotnet.Hcl.Common;

/// <summary>
/// Identifies the type of an HCL token produced by the reader.
/// </summary>
public enum HclTokenType : byte
{
    /// <summary>No token has been read yet.</summary>
    None = 0,

    // ── Structural ──────────────────────────────────────────────

    /// <summary>A newline character sequence (LF or CRLF).</summary>
    NewLine,

    /// <summary>End of the HCL input.</summary>
    Eof,

    // ── Block / Attribute ───────────────────────────────────────

    /// <summary>The opening of a block body (<c>{</c> following block type and labels).</summary>
    BlockStart,

    /// <summary>The closing of a block body (<c>}</c>).</summary>
    BlockEnd,

    /// <summary>A block type identifier (e.g., <c>resource</c>, <c>variable</c>).</summary>
    BlockType,

    /// <summary>A block label — a quoted string or bare identifier after the block type.</summary>
    BlockLabel,

    /// <summary>An attribute name (identifier before <c>=</c>).</summary>
    AttributeName,

    /// <summary>The <c>=</c> sign separating an attribute name from its value.</summary>
    Equals,

    // ── Literals ────────────────────────────────────────────────

    /// <summary>A quoted string literal value.</summary>
    StringLiteral,

    /// <summary>A numeric literal value (integer or float).</summary>
    NumberLiteral,

    /// <summary>A boolean literal (<c>true</c> or <c>false</c>).</summary>
    BoolLiteral,

    /// <summary>The <c>null</c> literal.</summary>
    NullLiteral,

    // ── Template ────────────────────────────────────────────────

    /// <summary>A template interpolation sequence (<c>${</c>).</summary>
    TemplateInterpolation,

    /// <summary>A template directive sequence (<c>%{</c>).</summary>
    TemplateDirective,

    /// <summary>The start of a heredoc string (<c>&lt;&lt;MARKER</c> or <c>&lt;&lt;-MARKER</c>).</summary>
    HeredocStart,

    /// <summary>The end marker of a heredoc string.</summary>
    HeredocEnd,

    // ── Delimiters ──────────────────────────────────────────────

    /// <summary>Opening brace <c>{</c> (collection context).</summary>
    OpenBrace,

    /// <summary>Closing brace <c>}</c> (collection context).</summary>
    CloseBrace,

    /// <summary>Opening bracket <c>[</c>.</summary>
    OpenBracket,

    /// <summary>Closing bracket <c>]</c>.</summary>
    CloseBracket,

    /// <summary>Opening parenthesis <c>(</c>.</summary>
    OpenParen,

    /// <summary>Closing parenthesis <c>)</c>.</summary>
    CloseParen,

    // ── Operators ───────────────────────────────────────────────

    /// <summary>The <c>+</c> operator.</summary>
    Plus,

    /// <summary>The <c>-</c> operator.</summary>
    Minus,

    /// <summary>The <c>*</c> operator.</summary>
    Star,

    /// <summary>The <c>/</c> operator.</summary>
    Slash,

    /// <summary>The <c>%</c> modulo operator.</summary>
    Percent,

    /// <summary>The <c>&amp;&amp;</c> logical AND operator.</summary>
    And,

    /// <summary>The <c>||</c> logical OR operator.</summary>
    Or,

    /// <summary>The <c>!</c> logical NOT operator.</summary>
    Not,

    /// <summary>The <c>==</c> equality operator.</summary>
    EqualEqual,

    /// <summary>The <c>!=</c> inequality operator.</summary>
    NotEqual,

    /// <summary>The <c>&lt;</c> less-than operator.</summary>
    LessThan,

    /// <summary>The <c>&gt;</c> greater-than operator.</summary>
    GreaterThan,

    /// <summary>The <c>&lt;=</c> less-than-or-equal operator.</summary>
    LessEqual,

    /// <summary>The <c>&gt;=</c> greater-than-or-equal operator.</summary>
    GreaterEqual,

    /// <summary>The <c>?</c> conditional operator.</summary>
    Question,

    /// <summary>The <c>:</c> colon separator.</summary>
    Colon,

    /// <summary>The <c>=&gt;</c> fat arrow (used in for-object expressions).</summary>
    FatArrow,

    /// <summary>The <c>...</c> ellipsis (argument expansion or grouping mode).</summary>
    Ellipsis,

    /// <summary>The <c>.</c> dot operator (attribute access).</summary>
    Dot,

    /// <summary>The <c>,</c> comma separator.</summary>
    Comma,

    /// <summary>The <c>${</c> template interpolation start.</summary>
    DollarBrace,

    /// <summary>The <c>%{</c> template directive start.</summary>
    PercentBrace,

    // ── Others ──────────────────────────────────────────────────

    /// <summary>A bare identifier (variable reference, keyword, etc.).</summary>
    Identifier,

    /// <summary>A comment (<c>//</c>, <c>#</c>, or <c>/* */</c>).</summary>
    Comment,

    /// <summary>A function call (identifier followed by <c>(</c>).</summary>
    FunctionCall,
}
