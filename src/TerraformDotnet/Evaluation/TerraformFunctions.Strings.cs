using System.Globalization;
using System.Text;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    private static void AddStrings(Dictionary<string, Entry> table)
    {
        Add(table, "chomp", 1, 1, c => Str(Chomp(c.String(0))));
        Add(table, "indent", 2, 2, Indent);
        Add(table, "join", 2, -1, Join);
        Add(table, "lower", 1, 1, c => Str(MapRunes(c.String(0), ToLowerSimple)));
        Add(table, "upper", 1, 1, c => Str(MapRunes(c.String(0), ToUpperSimple)));
        Add(table, "replace", 3, 3, Replace);
        Add(table, "split", 2, 2, Split);
        Add(table, "strrev", 1, 1, c => Str(string.Concat(Enumerable.Reverse(Graphemes(c.String(0))))));
        Add(table, "substr", 3, 3, Substr);
        Add(table, "title", 1, 1, c => Str(Title(c.String(0))));
        Add(table, "trim", 2, 2, c => Str(Trim(c.String(0), c.String(1))));
        Add(table, "trimprefix", 2, 2, c => Str(TrimPrefix(c.String(0), c.String(1))));
        Add(table, "trimsuffix", 2, 2, c => Str(TrimSuffix(c.String(0), c.String(1))));
        Add(table, "trimspace", 1, 1, c => Str(c.String(0).Trim()));
        Add(table, "startswith", 2, 2, c => Bool(c.String(0).StartsWith(c.String(1), StringComparison.Ordinal)));
        Add(table, "endswith", 2, 2, c => Bool(c.String(0).EndsWith(c.String(1), StringComparison.Ordinal)));
        Add(table, "strcontains", 2, 2, c => Bool(c.String(0).Contains(c.String(1), StringComparison.Ordinal)));
    }

    /// <summary>Splits text into user-perceived characters (extended grapheme clusters).</summary>
    internal static List<string> Graphemes(string text)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            result.Add((string)enumerator.Current);
        }

        return result;
    }

    // Go applies simple Unicode case mapping; .NET's invariant mapping differs for the dotted/dotless i.
    private static Rune ToLowerSimple(Rune rune) => rune.Value == 0x130 ? new Rune('i') : Rune.ToLowerInvariant(rune);

    private static Rune ToUpperSimple(Rune rune) => rune.Value == 0x131 ? new Rune('I') : Rune.ToUpperInvariant(rune);

    private static string MapRunes(string text, Func<Rune, Rune> map)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            builder.Append(map(rune).ToString());
        }

        return builder.ToString();
    }

    private static string Chomp(string text) => text.TrimEnd('\r', '\n');

    private static HclValue Indent(FunctionCall c)
    {
        var spaces = c.Int(0);
        if (spaces < 0)
        {
            throw c.Error("the number of spaces must not be negative.");
        }

        return Str(c.String(1).Replace("\n", "\n" + new string(' ', spaces), StringComparison.Ordinal));
    }

    private static HclValue Join(FunctionCall c)
    {
        var separator = c.String(0);
        var parts = new List<string>();
        for (var i = 1; i < c.Count; i++)
        {
            foreach (var item in c.List(i))
            {
                parts.Add(c.AsString(item, "list element"));
            }
        }

        return Str(string.Join(separator, parts));
    }

    private static HclValue Split(FunctionCall c)
    {
        var separator = c.String(0);
        var text = c.String(1);
        if (separator.Length == 0)
        {
            return List(text.EnumerateRunes().Select(r => Str(r.ToString())).ToList());
        }

        return List(text.Split(separator, StringSplitOptions.None).Select(Str).ToList());
    }

    private static HclValue Substr(FunctionCall c)
    {
        var characters = Graphemes(c.String(0));
        var offset = c.Int(1);
        var length = c.Int(2);

        if (offset < 0)
        {
            offset = Math.Max(0, characters.Count + offset);
        }

        if (offset >= characters.Count)
        {
            return Str(string.Empty);
        }

        var end = length < 0 ? characters.Count : (int)Math.Min((long)characters.Count, (long)offset + length);

        return Str(string.Concat(characters.Skip(offset).Take(end - offset)));
    }

    private static string Title(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previous = ' ';
        foreach (var current in text)
        {
            builder.Append(IsWordSeparator(previous) ? char.ToUpperInvariant(current) : current);
            previous = current;
        }

        return builder.ToString();
    }

    private static bool IsWordSeparator(char c)
    {
        if (c <= 0x7F)
        {
            return !(char.IsAsciiLetterOrDigit(c) || c == '_');
        }

        return !(char.IsLetterOrDigit(c)) && char.IsWhiteSpace(c);
    }

    private static string Trim(string text, string cutset)
        => cutset.Length == 0 ? text : text.Trim(cutset.ToCharArray());

    private static string TrimPrefix(string text, string prefix)
        => prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;

    private static string TrimSuffix(string text, string suffix)
        => suffix.Length > 0 && text.EndsWith(suffix, StringComparison.Ordinal) ? text[..^suffix.Length] : text;

    private static HclValue? Replace(FunctionCall c)
    {
        var text = c.String(0);
        var search = c.String(1);
        var replacement = c.String(2);

        if (search.Length >= 2 && search[0] == '/' && search[^1] == '/')
        {
            return TerraformRegex.Replace(c, text, search[1..^1], replacement);
        }

        if (search.Length == 0)
        {
            var builder = new StringBuilder();
            builder.Append(replacement);
            foreach (var rune in text.EnumerateRunes())
            {
                builder.Append(rune.ToString()).Append(replacement);
            }

            return Str(builder.ToString());
        }

        return Str(text.Replace(search, replacement, StringComparison.Ordinal));
    }
}
