using System.Text;
using System.Text.RegularExpressions;

namespace TerraformDotnet.Naming;

/// <summary>
/// A format for the names of one kind of Terraform block. It checks names against a pattern and
/// generates names in a <see cref="NameStyle"/>.
/// <example>
/// <code>
/// NameFormat.SnakeCase.IsMatch("web_app");        // true
/// NameFormat.SnakeCase.Format("dns", "Web-App");  // "dns_web_app"
///
/// var hyphens = NameFormat.Custom("^[a-z0-9-]+$");
/// hyphens.Format("dns", "web_app");               // "dns-web-app"
/// </code>
/// </example>
/// </summary>
public sealed class NameFormat : IEquatable<NameFormat>
{
    private const string SnakeCasePattern = "^[a-z][a-z0-9]*(_[a-z0-9]+)*$";
    private const string MixedSnakeCasePattern = "^[a-zA-Z][a-zA-Z0-9]*(_[a-zA-Z0-9]+)*$";
    private const string KebabCasePattern = "^[a-z][a-z0-9]*(-[a-z0-9]+)*$";
    private const string UncheckedName = "none";

    private static readonly TimeSpan BacktrackingTimeout = TimeSpan.FromSeconds(1);

    private static readonly NameStyle[] InferenceOrder =
        [NameStyle.SnakeCase, NameStyle.KebabCase, NameStyle.MixedSnakeCase];

    private readonly Regex? _regex;

    private NameFormat(string name, string? pattern, NameStyle style, bool isNamed, Regex? regex)
    {
        Name = name;
        Pattern = pattern;
        Style = style;
        IsNamed = isNamed;
        _regex = regex;
    }

    /// <summary>
    /// The <c>snake_case</c> format of tflint: lowercase letters and digits, words separated by one
    /// underscore, starting with a letter. This is the Terraform style.
    /// </summary>
    public static NameFormat SnakeCase { get; } = BuiltIn("snake_case", SnakeCasePattern, NameStyle.SnakeCase);

    /// <summary>
    /// The <c>mixed_snake_case</c> format of tflint: like <see cref="SnakeCase"/>, but letters may be
    /// upper or lower case.
    /// </summary>
    public static NameFormat MixedSnakeCase { get; } =
        BuiltIn("mixed_snake_case", MixedSnakeCasePattern, NameStyle.MixedSnakeCase);

    /// <summary>
    /// Lowercase letters and digits, words separated by one hyphen, starting with a letter. tflint has
    /// no format of this name; in a tflint configuration it is a custom format.
    /// </summary>
    public static NameFormat KebabCase { get; } = BuiltIn("kebab-case", KebabCasePattern, NameStyle.KebabCase);

    /// <summary>
    /// The <c>none</c> format of tflint: names are not checked. Names are generated in
    /// <see cref="NameStyle.SnakeCase"/>.
    /// </summary>
    public static NameFormat None { get; } = Unchecked(NameStyle.SnakeCase);

    /// <summary>
    /// Gets the name shown for the format: the name of a built-in format, the description of a custom
    /// format, or its pattern when it has no description.
    /// </summary>
    public string Name { get; }

    /// <summary>Gets the regular expression names must match, or <c>null</c> when names are not checked.</summary>
    public string? Pattern { get; }

    /// <summary>Gets the style names are generated in.</summary>
    public NameStyle Style { get; }

    /// <summary>Gets whether names are checked against a <see cref="Pattern"/>.</summary>
    public bool IsChecked => _regex is not null;

    /// <summary>Whether <see cref="Name"/> is a name or description rather than the pattern itself.</summary>
    internal bool IsNamed { get; }

    /// <summary>Creates a format that does not check names and generates them in <paramref name="style"/>.</summary>
    /// <param name="style">The style names are generated in.</param>
    public static NameFormat Unchecked(NameStyle style) => new(UncheckedName, null, style, isNamed: true, null);

    /// <summary>
    /// Creates a format from a regular expression.
    /// </summary>
    /// <param name="pattern">
    /// The regular expression a name must match. Like tflint, a name matches when the pattern matches
    /// any part of it, so anchor the pattern with <c>^</c> and <c>$</c>. Patterns are .NET regular
    /// expressions; the RE2 syntax of tflint is accepted except for POSIX classes
    /// (<c>[[:alpha:]]</c>), <c>(?P&lt;name&gt;…)</c>, <c>\Q…\E</c> and <c>\pL</c>.
    /// </param>
    /// <param name="style">
    /// The style names are generated in. When <c>null</c>, it is the first of snake_case, kebab-case
    /// and mixed snake_case the pattern accepts, or snake_case when it accepts none of them.
    /// </param>
    /// <param name="description">
    /// A description of the format, used as its <see cref="Name"/>. Defaults to the pattern.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is not a valid regular expression.</exception>
    public static NameFormat Custom(string pattern, NameStyle? style = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Regex regex = Compile(pattern);

        return Custom(pattern, regex, style ?? InferStyle(regex, preferred: null), description);
    }

    /// <summary>
    /// Creates a custom format that generates names in <paramref name="preferred"/> when the pattern
    /// accepts that style, and otherwise in the first style it accepts.
    /// </summary>
    internal static NameFormat CustomPreferring(string pattern, NameStyle preferred, string? description)
    {
        Regex regex = Compile(pattern);

        return Custom(pattern, regex, InferStyle(regex, preferred), description);
    }

    /// <summary>
    /// Checks whether a name has this format. A format that does not check names matches every name.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <exception cref="RegexMatchTimeoutException">
    /// A custom pattern that needs backtracking (lookarounds or backreferences) took more than a second.
    /// </exception>
    public bool IsMatch(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _regex is null || _regex.IsMatch(name);
    }

    /// <summary>
    /// Builds a name from parts. The parts are joined with the separator of the <see cref="Style"/>.
    /// A result that is a Terraform identifier and matches the format is returned as it is, so names
    /// that already conform never change. Otherwise the result is normalized: it is lowercased
    /// (except in <see cref="NameStyle.MixedSnakeCase"/>), every run of characters other than ASCII
    /// letters and digits becomes one separator, and separators at the start and end are removed.
    /// Words are not split: <c>MyApp</c> becomes <c>myapp</c>.
    /// <para>
    /// Nothing is added to a name, so the result can still fail the format, for example when it
    /// starts with a digit or a custom pattern requires a prefix. Check it with <see cref="IsMatch"/>.
    /// </para>
    /// </summary>
    /// <param name="parts">The parts of the name. <c>null</c> and empty parts are skipped.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentException">The parts contain no letter or digit.</exception>
    public string Format(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        char separator = Style == NameStyle.KebabCase ? '-' : '_';
        string joined = string.Join(separator, parts.Where(part => !string.IsNullOrEmpty(part)));

        if (IsIdentifier(joined) && IsMatch(joined))
        {
            return joined;
        }

        string normalized = Normalize(joined, separator, lowercase: Style != NameStyle.MixedSnakeCase);

        if (normalized.Length == 0)
        {
            throw new ArgumentException("The parts contain no letter or digit to build a name from.", nameof(parts));
        }

        return normalized;
    }

    /// <inheritdoc />
    public bool Equals(NameFormat? other) =>
        other is not null
        && Name == other.Name
        && Pattern == other.Pattern
        && Style == other.Style
        && IsNamed == other.IsNamed;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as NameFormat);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Pattern, Style, IsNamed);

    /// <summary>Returns the <see cref="Name"/> of the format.</summary>
    public override string ToString() => Name;

    /// <summary>
    /// Whether <paramref name="name"/> can be used as a Terraform name: an ASCII letter or underscore
    /// followed by ASCII letters, digits, underscores and hyphens.
    /// </summary>
    internal static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static NameFormat BuiltIn(string name, string pattern, NameStyle style) =>
        new(name, pattern, style, isNamed: true, Compile(pattern));

    private static NameFormat Custom(string pattern, Regex regex, NameStyle style, string? description)
    {
        bool isNamed = !string.IsNullOrEmpty(description);

        return new NameFormat(isNamed ? description! : pattern, pattern, style, isNamed, regex);
    }

    // tflint matches with RE2, which runs in linear time and has no lookarounds or backreferences.
    // The non-backtracking engine is its counterpart; other patterns run with a timeout.
    private static Regex Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, BacktrackingTimeout);
        }
    }

    private static NameStyle InferStyle(Regex regex, NameStyle? preferred)
    {
        if (preferred is { } style && regex.IsMatch(Sample(style)))
        {
            return style;
        }

        foreach (NameStyle candidate in InferenceOrder)
        {
            if (regex.IsMatch(Sample(candidate)))
            {
                return candidate;
            }
        }

        return preferred ?? NameStyle.SnakeCase;
    }

    private static string Sample(NameStyle style) =>
        style switch
        {
            NameStyle.KebabCase => "ab-cd",
            NameStyle.MixedSnakeCase => "Ab_Cd",
            _ => "ab_cd",
        };

    private static string Normalize(string value, char separator, bool lowercase)
    {
        var builder = new StringBuilder(value.Length);
        bool pendingSeparator = false;

        foreach (char c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                builder.Append(separator);
                pendingSeparator = false;
            }

            builder.Append(lowercase ? char.ToLowerInvariant(c) : c);
        }

        return builder.ToString();
    }
}
