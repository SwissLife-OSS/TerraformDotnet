using System.Globalization;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Represents a literal value: string, number, boolean, or null.
/// </summary>
public sealed class HclLiteralExpression : HclExpression
{
    /// <summary>Gets or sets the literal value as a string representation.</summary>
    public required string? Value { get; set; }

    /// <summary>Gets or sets the literal kind.</summary>
    public required HclLiteralKind Kind { get; set; }

    /// <summary>
    /// Creates a string literal. Quotes, backslashes and control characters are escaped when written.
    /// Template sequences are kept as written, the same as for parsed literals: <c>${...}</c> and
    /// <c>%{...}</c> stay template code, and <c>$${</c> and <c>%%{</c> stay literal text.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>The literal.</returns>
    public static HclLiteralExpression String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new() { Value = value, Kind = HclLiteralKind.String };
    }

    /// <summary>Creates a number literal.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The literal.</returns>
    public static HclLiteralExpression Number(int value) => Number((long)value);

    /// <summary>Creates a number literal.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The literal.</returns>
    public static HclLiteralExpression Number(long value) =>
        new() { Value = value.ToString(CultureInfo.InvariantCulture), Kind = HclLiteralKind.Number };

    /// <summary>Creates a number literal, written in plain decimal notation (never with an exponent).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The literal.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite.</exception>
    public static HclLiteralExpression Number(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "HCL has no literal for NaN or infinity.");
        }

        return new() { Value = HclNumberFormatter.Format(value), Kind = HclLiteralKind.Number };
    }

    /// <summary>Creates a number literal.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The literal.</returns>
    public static HclLiteralExpression Number(decimal value) =>
        new() { Value = value.ToString(CultureInfo.InvariantCulture), Kind = HclLiteralKind.Number };

    /// <summary>Creates a <c>true</c> or <c>false</c> literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The literal.</returns>
    public static HclLiteralExpression Bool(bool value) =>
        new() { Value = value ? "true" : "false", Kind = HclLiteralKind.Bool };

    /// <summary>Creates the <c>null</c> literal.</summary>
    /// <returns>The literal.</returns>
    public static HclLiteralExpression Null() => new() { Value = null, Kind = HclLiteralKind.Null };

    /// <inheritdoc />
    public override void Accept(IHclVisitor visitor) => visitor.VisitLiteral(this);

    /// <inheritdoc />
    public override HclNode DeepClone() => new HclLiteralExpression
    {
        Value = Value,
        Kind = Kind,
        Start = Start,
        End = End,
    };
}

/// <summary>
/// Classifies the kind of a literal expression.
/// </summary>
public enum HclLiteralKind : byte
{
    /// <summary>A string value.</summary>
    String,

    /// <summary>A numeric value (integer or float).</summary>
    Number,

    /// <summary>A boolean value.</summary>
    Bool,

    /// <summary>A null value.</summary>
    Null,
}
