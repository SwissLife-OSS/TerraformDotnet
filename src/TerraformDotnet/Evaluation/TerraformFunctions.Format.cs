using System.Globalization;
using System.Numerics;
using System.Text;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    private static void AddFormat(Dictionary<string, Entry> table)
    {
        Add(table, "format", 1, -1, Format);
        Add(table, "formatlist", 1, -1, FormatList);
    }

    private static HclValue? Format(FunctionCall c)
    {
        var text = FormatCore(c, c.String(0), c.Values.Skip(1).ToList());

        return text is null ? null : Str(text);
    }

    private static HclValue? FormatList(FunctionCall c)
    {
        var template = c.String(0);
        var arguments = c.Values.Skip(1).ToList();

        int? length = null;
        foreach (var argument in arguments)
        {
            if (argument.Type != HclValueType.Tuple)
            {
                continue;
            }

            if (length is not null && length != argument.TupleValue.Count)
            {
                throw c.Error("all list arguments must have the same length.");
            }

            length = argument.TupleValue.Count;
        }

        var count = length ?? 1;
        var result = new List<HclValue>(count);
        for (var i = 0; i < count; i++)
        {
            var row = arguments.Select(a => a.Type == HclValueType.Tuple ? a.TupleValue[i] : a).ToList();
            var text = FormatCore(c, template, row);
            if (text is null)
            {
                return null;
            }

            result.Add(Str(text));
        }

        return List(result);
    }

    /// <summary>
    /// Formats like Terraform's <c>format</c>. Returns <c>null</c> for verbs, flags or argument
    /// combinations this implementation does not reproduce exactly, so the result stays unknown.
    /// </summary>
    private static string? FormatCore(FunctionCall c, string template, IReadOnlyList<HclValue> arguments)
    {
        var builder = new StringBuilder();
        var next = 0;
        var usedExplicitIndex = false;
        var used = new HashSet<int>();

        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '%')
            {
                builder.Append(template[i]);
                continue;
            }

            i++;
            if (i >= template.Length)
            {
                return null;
            }

            if (template[i] == '%')
            {
                builder.Append('%');
                continue;
            }

            var spec = new FormatSpec();
            if (!ParseSpec(template, ref i, spec, ref usedExplicitIndex, ref next))
            {
                return null;
            }

            if (spec.Verb == '\0')
            {
                return null;
            }

            if (next >= arguments.Count)
            {
                throw c.Error($"not enough arguments for verb %{spec.Verb}: need argument {next + 1} but have {arguments.Count} in total.");
            }

            var formatted = FormatOne(c, spec, arguments[next]);
            if (formatted is null)
            {
                return null;
            }

            used.Add(next);
            next++;
            builder.Append(Pad(formatted, spec));
        }

        if (used.Count < arguments.Count)
        {
            if (usedExplicitIndex)
            {
                return null;
            }

            throw c.Error($"too many arguments; only {used.Count} used but {arguments.Count} given.");
        }

        return builder.ToString();
    }

    private sealed class FormatSpec
    {
        public bool LeftAlign { get; set; }

        public bool ForceSign { get; set; }

        public bool SpaceSign { get; set; }

        public bool ZeroPad { get; set; }

        public int? Width { get; set; }

        public int? Precision { get; set; }

        public char Verb { get; set; }
    }

    private static bool ParseSpec(string template, ref int i, FormatSpec spec, ref bool usedExplicitIndex, ref int next)
    {
        if (!TryParseIndex(template, ref i, ref usedExplicitIndex, ref next))
        {
            return false;
        }

        for (; i < template.Length; i++)
        {
            switch (template[i])
            {
                case '-':
                    spec.LeftAlign = true;
                    continue;
                case '+':
                    spec.ForceSign = true;
                    continue;
                case ' ':
                    spec.SpaceSign = true;
                    continue;
                case '0':
                    spec.ZeroPad = true;
                    continue;
                case '#':
                    return false;
            }

            break;
        }

        spec.Width = ReadNumber(template, ref i);
        if (i < template.Length && template[i] == '.')
        {
            i++;
            spec.Precision = ReadNumber(template, ref i) ?? 0;
        }

        if (!TryParseIndex(template, ref i, ref usedExplicitIndex, ref next) || i >= template.Length)
        {
            return false;
        }

        spec.Verb = template[i];

        return true;
    }

    private static bool TryParseIndex(string template, ref int i, ref bool usedExplicitIndex, ref int next)
    {
        if (i >= template.Length || template[i] != '[')
        {
            return true;
        }

        var close = template.IndexOf(']', i);
        if (close < 0 || !int.TryParse(template.AsSpan(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 1)
        {
            return false;
        }

        usedExplicitIndex = true;
        next = index - 1;
        i = close + 1;

        return true;
    }

    private static int? ReadNumber(string template, ref int i)
    {
        var start = i;
        while (i < template.Length && char.IsAsciiDigit(template[i]))
        {
            i++;
        }

        return i > start && int.TryParse(template.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? FormatOne(FunctionCall c, FormatSpec spec, HclValue argument)
    {
        switch (spec.Verb)
        {
            case 's':
                return Truncate(c.AsString(argument, "the %s argument"), spec);
            case 'v':
                return argument.Type switch
                {
                    HclValueType.Tuple or HclValueType.Object => TerraformValues.JsonEncode(argument),
                    HclValueType.Number => spec.Precision is null ? FormatShortestGeneral(argument.NumberValue) : null,
                    _ => Truncate(argument.ToHclString(), spec),
                };
            case 'q':
                return Quote(c.AsString(argument, "the %q argument"));
            case 't':
                return c.AsBool(argument, "the %t argument") ? "true" : "false";
            case 'd':
                return FormatInteger(c, spec, argument, 10, upper: false);
            case 'x':
                return argument.Type == HclValueType.String ? null : FormatInteger(c, spec, argument, 16, upper: false);
            case 'X':
                return argument.Type == HclValueType.String ? null : FormatInteger(c, spec, argument, 16, upper: true);
            case 'o':
                return argument.Type == HclValueType.String ? null : FormatInteger(c, spec, argument, 8, upper: false);
            case 'b':
                return argument.Type == HclValueType.String ? null : FormatInteger(c, spec, argument, 2, upper: false);
            case 'f':
                return FormatFixed(c, spec, argument);
            default:
                return null;
        }
    }

    /// <summary>Formats a number like Go's <c>%v</c>/<c>%g</c> with the shortest round-trip digits.</summary>
    private static string FormatShortestGeneral(double number)
    {
        if (number == 0)
        {
            return "0";
        }

        var text = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var pointPosition = (dot < 0 ? text.Length : dot) + exponent;
        var digits = text.Replace(".", string.Empty, StringComparison.Ordinal);
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits.TrimStart('0').TrimEnd('0');
        pointPosition -= leadingZeros;

        var scientificExponent = pointPosition - 1;
        string body;
        if (scientificExponent < -4 || scientificExponent >= 6)
        {
            var mantissa = digits.Length > 1 ? digits[0] + "." + digits[1..] : digits;
            var sign = scientificExponent < 0 ? "-" : "+";
            body = mantissa + "e" + sign + Math.Abs(scientificExponent).ToString("00", CultureInfo.InvariantCulture);
        }
        else if (pointPosition <= 0)
        {
            body = "0." + new string('0', -pointPosition) + digits;
        }
        else if (pointPosition >= digits.Length)
        {
            body = digits + new string('0', pointPosition - digits.Length);
        }
        else
        {
            body = digits[..pointPosition] + "." + digits[pointPosition..];
        }

        return number < 0 ? "-" + body : body;
    }

    private static string Truncate(string text, FormatSpec spec)
    {
        if (spec.Precision is not { } precision)
        {
            return text;
        }

        var runes = text.EnumerateRunes().Take(precision).Select(r => r.ToString());

        return string.Concat(runes);
    }

    private static string? FormatInteger(FunctionCall c, FormatSpec spec, HclValue argument, int radix, bool upper)
    {
        if (spec.Precision is not null)
        {
            return null;
        }

        var number = c.AsNumber(argument, "the numeric verb argument");
        if (number != Math.Floor(number))
        {
            throw c.Error("the argument must be a whole number for an integer verb.");
        }

        var value = new BigInteger(number);
        var digits = ToRadix(BigInteger.Abs(value), radix);
        if (upper)
        {
            digits = digits.ToUpperInvariant();
        }

        return ApplySign(digits, value.Sign < 0, spec);
    }

    private static string? FormatFixed(FunctionCall c, FormatSpec spec, HclValue argument)
    {
        var number = c.AsNumber(argument, "the %f argument");
        var digits = Math.Abs(number).ToString("F" + (spec.Precision ?? 6).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        return ApplySign(digits, number < 0, spec);
    }

    private static string ApplySign(string digits, bool negative, FormatSpec spec)
    {
        var sign = negative ? "-" : spec.ForceSign ? "+" : spec.SpaceSign ? " " : string.Empty;
        if (spec.ZeroPad && !spec.LeftAlign && spec.Width is { } width && sign.Length + digits.Length < width)
        {
            digits = new string('0', width - sign.Length - digits.Length) + digits;
        }

        return sign + digits;
    }

    private static string ToRadix(BigInteger value, int radix)
    {
        if (value.IsZero)
        {
            return "0";
        }

        var builder = new StringBuilder();
        while (!value.IsZero)
        {
            builder.Insert(0, "0123456789abcdef"[(int)(value % radix)]);
            value /= radix;
        }

        return builder.ToString();
    }

    private static string Pad(string text, FormatSpec spec)
    {
        if (spec.Width is not { } width)
        {
            return text;
        }

        var length = text.EnumerateRunes().Count();
        if (length >= width)
        {
            return text;
        }

        var padding = new string(spec.ZeroPad && !spec.LeftAlign && spec.Verb is 's' or 'v' or 'q' or 't' ? '0' : ' ', width - length);

        return spec.LeftAlign ? text + new string(' ', width - length) : padding + text;
    }

    private static string? Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case < 0x20 or 0x7f:
                    builder.Append("\\x").Append(rune.Value.ToString("x2", CultureInfo.InvariantCulture));
                    break;
                default:
                    if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator && rune.Value != ' ')
                    {
                        return null;
                    }

                    builder.Append(rune.ToString());
                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
