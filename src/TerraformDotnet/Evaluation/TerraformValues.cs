using System.Globalization;
using System.Text;
using System.Text.Json;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

/// <summary>
/// Shared value helpers for the Terraform function library and the type converter:
/// number parsing, canonical set ordering, type unification and JSON conversion.
/// </summary>
internal static class TerraformValues
{
    /// <summary>Parses a Terraform number literal string (no whitespace, no hex, no infinities).</summary>
    public static bool TryParseNumber(string text, out double number)
    {
        if (double.TryParse(
                text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out number)
            && double.IsFinite(number))
        {
            return true;
        }

        number = 0;

        return false;
    }

    /// <summary>
    /// Builds a canonical set: distinct elements in Terraform's stable order. Sets are modeled as
    /// sorted, de-duplicated tuples so equality and iteration order match Terraform.
    /// </summary>
    public static HclValue ToSet(IEnumerable<HclValue> elements)
    {
        var sorted = new List<HclValue>();
        foreach (var element in elements)
        {
            if (!sorted.Contains(element))
            {
                sorted.Add(element);
            }
        }

        sorted.Sort(Compare);

        return HclValue.FromTuple(sorted);
    }

    /// <summary>
    /// Orders values the way Terraform orders set elements: by kind (null, bool, number, string,
    /// tuple, object), then by value.
    /// </summary>
    public static int Compare(HclValue left, HclValue right)
    {
        var byRank = Rank(left).CompareTo(Rank(right));
        if (byRank != 0)
        {
            return byRank;
        }

        switch (left.Type)
        {
            case HclValueType.Bool:
                return left.BoolValue.CompareTo(right.BoolValue);
            case HclValueType.Number:
                return left.NumberValue.CompareTo(right.NumberValue);
            case HclValueType.String:
                return string.CompareOrdinal(left.StringValue, right.StringValue);
            case HclValueType.Tuple:
                var count = Math.Min(left.TupleValue.Count, right.TupleValue.Count);
                for (var i = 0; i < count; i++)
                {
                    var byElement = Compare(left.TupleValue[i], right.TupleValue[i]);
                    if (byElement != 0)
                    {
                        return byElement;
                    }
                }

                return left.TupleValue.Count.CompareTo(right.TupleValue.Count);
            case HclValueType.Object:
                return string.CompareOrdinal(JsonEncode(left), JsonEncode(right));
            default:
                return 0;
        }
    }

    private static int Rank(HclValue value) => value.Type switch
    {
        HclValueType.Null => 0,
        HclValueType.Bool => 1,
        HclValueType.Number => 2,
        HclValueType.String => 3,
        HclValueType.Tuple => 4,
        HclValueType.Object => 5,
        _ => 6,
    };

    /// <summary>
    /// Terraform converts the elements of a list, set or map to one common type; a mix of
    /// primitive kinds becomes strings. Other shapes are returned unchanged.
    /// </summary>
    public static IReadOnlyList<HclValue> UnifyPrimitives(IReadOnlyList<HclValue> elements)
    {
        var unify = CreateUnifier(elements);

        return ReferenceEquals(unify, Identity) ? elements : elements.Select(unify).ToList();
    }

    /// <summary>
    /// Creates the per-element conversion that brings all given elements to one common primitive
    /// type: identity unless primitives of different kinds are mixed, in which case everything
    /// becomes a string.
    /// </summary>
    public static Func<HclValue, HclValue> CreateUnifier(IEnumerable<HclValue> elements)
    {
        var kinds = new HashSet<HclValueType>();
        foreach (var element in elements)
        {
            if (element.Type == HclValueType.Null)
            {
                continue;
            }

            if (element.Type is not (HclValueType.String or HclValueType.Number or HclValueType.Bool))
            {
                return Identity;
            }

            kinds.Add(element.Type);
        }

        return kinds.Count < 2 ? Identity : ToStringElement;
    }

    private static readonly Func<HclValue, HclValue> Identity = static v => v;

    private static readonly Func<HclValue, HclValue> ToStringElement = static v =>
        v.Type is HclValueType.Number or HclValueType.Bool ? HclValue.FromString(v.ToHclString()) : v;

    /// <summary>Encodes a value as JSON like Terraform's <c>jsonencode</c> (sorted keys, HTML-safe escapes).</summary>
    public static string JsonEncode(HclValue value)
    {
        var sb = new StringBuilder();
        WriteJson(sb, value);

        return sb.ToString();
    }

    private static void WriteJson(StringBuilder sb, HclValue value)
    {
        switch (value.Type)
        {
            case HclValueType.Null:
                sb.Append("null");
                break;
            case HclValueType.Bool:
                sb.Append(value.BoolValue ? "true" : "false");
                break;
            case HclValueType.Number:
                sb.Append(value.ToHclString());
                break;
            case HclValueType.String:
                WriteJsonString(sb, value.StringValue);
                break;
            case HclValueType.Tuple:
                sb.Append('[');
                for (var i = 0; i < value.TupleValue.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    WriteJson(sb, value.TupleValue[i]);
                }

                sb.Append(']');
                break;
            case HclValueType.Object:
                sb.Append('{');
                var first = true;
                foreach (var key in value.ObjectValue.Keys.Order(StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    WriteJsonString(sb, key);
                    sb.Append(':');
                    WriteJson(sb, value.ObjectValue[key]);
                }

                sb.Append('}');
                break;
            default:
                throw new InvalidOperationException("Unknown values cannot be encoded as JSON.");
        }
    }

    private static void WriteJsonString(StringBuilder sb, string text)
    {
        sb.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '<' or '>' or '&' or '\u2028' or '\u2029':
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                case < ' ':
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
    }

    /// <summary>Converts a parsed JSON element into an <see cref="HclValue"/>.</summary>
    public static HclValue FromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var entries = new Dictionary<string, HclValue>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    entries[property.Name] = FromJson(property.Value);
                }

                return HclValue.FromObject(entries);
            case JsonValueKind.Array:
                return HclValue.FromTuple(element.EnumerateArray().Select(FromJson).ToList());
            case JsonValueKind.String:
                return HclValue.FromString(element.GetString()!);
            case JsonValueKind.Number:
                return HclValue.FromNumber(element.GetDouble());
            case JsonValueKind.True:
                return HclValue.True;
            case JsonValueKind.False:
                return HclValue.False;
            default:
                return HclValue.Null;
        }
    }
}
