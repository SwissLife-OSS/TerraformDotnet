using System.Text;
using System.Text.Json;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static void AddEncoding(Dictionary<string, Entry> table)
    {
        Add(table, "jsonencode", 1, 1, c => Str(TerraformValues.JsonEncode(c.Any(0))));
        Add(table, "jsondecode", 1, 1, JsonDecode);
        Add(table, "base64encode", 1, 1, c => Str(Convert.ToBase64String(Encoding.UTF8.GetBytes(c.String(0)))));
        Add(table, "base64decode", 1, 1, Base64Decode);
        Add(table, "urlencode", 1, 1, c => Str(UrlEncode(c.String(0))));
    }

    private static HclValue JsonDecode(FunctionCall c)
    {
        try
        {
            using var document = JsonDocument.Parse(c.String(0));

            return TerraformValues.FromJson(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw c.Error($"the given value is not valid JSON: {exception.Message}");
        }
    }

    private static HclValue Base64Decode(FunctionCall c)
    {
        var text = c.String(0).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
        var valid = text.Length % 4 == 0 && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '+' or '/' or '=');
        if (!valid)
        {
            throw c.Error("the given value is not valid base64.");
        }

        try
        {
            return Str(StrictUtf8.GetString(Convert.FromBase64String(text)));
        }
        catch (FormatException)
        {
            throw c.Error("the given value is not valid base64.");
        }
        catch (ArgumentException)
        {
            throw c.Error("the result of decoding the provided string is not valid UTF-8.");
        }
    }

    /// <summary>Percent-encodes like Go's <c>url.QueryEscape</c>: spaces become <c>+</c>, only <c>A-Za-z0-9-_.~</c> stay.</summary>
    private static string UrlEncode(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            var ch = (char)value;
            if (value < 0x80 && (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or '~'))
            {
                builder.Append(ch);
            }
            else if (ch == ' ')
            {
                builder.Append('+');
            }
            else
            {
                builder.Append('%').Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
