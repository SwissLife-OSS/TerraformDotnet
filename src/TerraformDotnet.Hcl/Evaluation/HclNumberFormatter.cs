using System.Globalization;
using System.Text;

namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Formats numbers the way Terraform renders them in string conversions: never in exponent notation.
/// </summary>
internal static class HclNumberFormatter
{
    /// <summary>Formats a number using the shortest round-trippable representation without an exponent.</summary>
    /// <param name="value">The number to format.</param>
    /// <returns>The plain decimal text, or <c>+Inf</c>, <c>-Inf</c>, <c>NaN</c> for non-finite values.</returns>
    public static string Format(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "+Inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Inf";
        }

        if (value == 0)
        {
            return "0";
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E');

        return exponentIndex < 0 ? text : ExpandExponent(text, exponentIndex);
    }

    /// <summary>Rewrites <c>d.dddE±xx</c> as plain decimal notation.</summary>
    private static string ExpandExponent(string text, int exponentIndex)
    {
        var mantissa = text[..exponentIndex];
        var exponent = int.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);

        var negative = mantissa.StartsWith('-');
        if (negative)
        {
            mantissa = mantissa[1..];
        }

        var dot = mantissa.IndexOf('.');
        var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        var pointPosition = (dot < 0 ? mantissa.Length : dot) + exponent;

        var sb = new StringBuilder();
        if (negative)
        {
            sb.Append('-');
        }

        if (pointPosition <= 0)
        {
            sb.Append("0.").Append('0', -pointPosition).Append(digits);
        }
        else if (pointPosition >= digits.Length)
        {
            sb.Append(digits).Append('0', pointPosition - digits.Length);
        }
        else
        {
            sb.Append(digits, 0, pointPosition).Append('.').Append(digits, pointPosition, digits.Length - pointPosition);
        }

        return sb.ToString();
    }
}
