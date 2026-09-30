using System.Globalization;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Validation;

/// <summary>
/// An immutable literal value (string, number, bool or null) that appears in a variable constraint
/// or predicate. It is deliberately independent from the mutable HCL AST so that constraints can be
/// stored, compared and serialised safely.
/// <example>
/// <code>
/// var gold = TerraformLiteral.FromString("gold");
/// Console.WriteLine(gold.Kind);  // String
/// Console.WriteLine(gold.Value); // gold
/// </code>
/// </example>
/// </summary>
/// <param name="Kind">The literal kind.</param>
/// <param name="Value">
/// The literal value as text. Numbers use the invariant culture, booleans are <c>true</c>/<c>false</c>,
/// and <c>null</c> literals carry no value.
/// </param>
public sealed record TerraformLiteral(HclLiteralKind Kind, string? Value)
{
    /// <summary>Gets the <c>null</c> literal.</summary>
    public static TerraformLiteral Null { get; } = new(HclLiteralKind.Null, null);

    /// <summary>Gets whether this literal is <c>null</c>.</summary>
    public bool IsNull => Kind == HclLiteralKind.Null;

    /// <summary>Creates a string literal.</summary>
    /// <param name="value">The string value.</param>
    public static TerraformLiteral FromString(string value) => new(HclLiteralKind.String, value);

    /// <summary>Creates a number literal.</summary>
    /// <param name="value">The numeric value.</param>
    public static TerraformLiteral FromNumber(decimal value) =>
        new(HclLiteralKind.Number, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a boolean literal.</summary>
    /// <param name="value">The boolean value.</param>
    public static TerraformLiteral FromBool(bool value) =>
        new(HclLiteralKind.Bool, value ? "true" : "false");

    /// <summary>Tries to read this literal as a number.</summary>
    /// <param name="number">The parsed number when the method returns <c>true</c>.</param>
    /// <returns><c>true</c> when this is a number literal with a valid value.</returns>
    public bool TryGetNumber(out decimal number)
    {
        number = 0;

        return Kind == HclLiteralKind.Number
            && decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Compares two literals with Terraform's <c>==</c> semantics: values of different kinds are never
    /// equal, numbers are compared numerically and strings ordinally.
    /// </summary>
    /// <param name="other">The literal to compare with.</param>
    /// <returns><c>true</c> when Terraform would evaluate <c>this == other</c> to <c>true</c>.</returns>
    public bool TerraformEquals(TerraformLiteral other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            HclLiteralKind.Null => true,
            HclLiteralKind.Number => TryGetNumber(out var a) && other.TryGetNumber(out var b)
                ? a == b
                : string.Equals(Value, other.Value, StringComparison.Ordinal),
            _ => string.Equals(Value, other.Value, StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// Converts an HCL expression into a literal. Handles plain literals and negated number literals
    /// (<c>-1</c>).
    /// </summary>
    internal static bool TryFromExpression(HclExpression expression, out TerraformLiteral literal)
    {
        switch (expression)
        {
            case HclLiteralExpression lit:
                literal = lit.Kind switch
                {
                    HclLiteralKind.Null => Null,
                    HclLiteralKind.Bool => FromBool(string.Equals(lit.Value, "true", StringComparison.OrdinalIgnoreCase)),
                    _ => new TerraformLiteral(lit.Kind, lit.Value),
                };

                return true;

            case HclUnaryExpression { Operator: HclUnaryOperator.Negate, Operand: HclLiteralExpression { Kind: HclLiteralKind.Number } num }
                when decimal.TryParse(num.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value):
                literal = FromNumber(-value);

                return true;

            default:
                literal = Null;

                return false;
        }
    }
}
