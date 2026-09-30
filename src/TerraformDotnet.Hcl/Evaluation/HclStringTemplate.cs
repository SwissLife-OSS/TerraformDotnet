using System.Runtime.CompilerServices;
using System.Text;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// The parsed form of a quoted string or heredoc that contains <c>${...}</c> interpolations.
/// </summary>
/// <remarks>
/// The reader keeps template sequences as raw text inside string literals; this type splits them
/// into literal text and interpolated expressions. <c>$${</c> and <c>%%{</c> are the escapes for
/// a literal <c>${</c> and <c>%{</c>. Template directives (<c>%{if}</c>, <c>%{for}</c>) and
/// whitespace-trimming markers (<c>~</c>) are not supported and yield no plan.
/// </remarks>
internal sealed class HclStringTemplate
{
    private static readonly ConditionalWeakTable<HclLiteralExpression, HclStringTemplate?> Cache = new();

    private HclStringTemplate(List<object> parts) => Parts = parts;

    /// <summary>Gets the parts: <see cref="string"/> for literal text, <see cref="HclExpression"/> for interpolations.</summary>
    public IReadOnlyList<object> Parts { get; }

    /// <summary>Determines whether the text may contain template sequences.</summary>
    public static bool MayBeTemplate(string text)
        => text.Contains("${", StringComparison.Ordinal) || text.Contains("%{", StringComparison.Ordinal);

    /// <summary>Gets the template plan of a string literal, or <c>null</c> when it cannot be evaluated.</summary>
    public static HclStringTemplate? For(HclLiteralExpression literal)
    {
        if (Cache.TryGetValue(literal, out var cached))
        {
            return cached;
        }

        var plan = Parse(literal.Value ?? string.Empty);
        Cache.AddOrUpdate(literal, plan);

        return plan;
    }

    private static HclStringTemplate? Parse(string text)
    {
        var parts = new List<object>();
        var literal = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            if (Starts(text, index, "$${"))
            {
                literal.Append("${");
                index += 3;
            }
            else if (Starts(text, index, "%%{"))
            {
                literal.Append("%{");
                index += 3;
            }
            else if (Starts(text, index, "%{"))
            {
                return null;
            }
            else if (Starts(text, index, "${"))
            {
                var end = FindInterpolationEnd(text, index + 2);
                if (end < 0)
                {
                    return null;
                }

                var inner = text[(index + 2)..end];
                if (inner.StartsWith('~') || inner.EndsWith('~') || !HclExpression.TryParse(inner, out var expression) || expression is null)
                {
                    return null;
                }

                if (literal.Length > 0)
                {
                    parts.Add(literal.ToString());
                    literal.Clear();
                }

                parts.Add(expression);
                index = end + 1;
            }
            else
            {
                literal.Append(text[index]);
                index++;
            }
        }

        if (literal.Length > 0)
        {
            parts.Add(literal.ToString());
        }

        return new HclStringTemplate(parts);
    }

    private static bool Starts(string text, int index, string marker)
        => string.CompareOrdinal(text, index, marker, 0, marker.Length) == 0;

    /// <summary>Finds the <c>}</c> closing an interpolation, skipping nested braces and strings.</summary>
    private static int FindInterpolationEnd(string text, int start)
    {
        var depth = 1;
        for (var index = start; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }

                    break;
                case '"':
                    index = SkipString(text, index + 1);
                    if (index < 0)
                    {
                        return -1;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>Returns the index of the closing quote of a nested string that starts at <paramref name="start"/>.</summary>
    private static int SkipString(string text, int start)
    {
        for (var index = start; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '\\':
                    index++;
                    break;
                case '"':
                    return index;
                case '$' when index + 1 < text.Length && text[index + 1] == '{':
                    index = FindInterpolationEnd(text, index + 2);
                    if (index < 0)
                    {
                        return -1;
                    }

                    break;
            }
        }

        return -1;
    }
}
