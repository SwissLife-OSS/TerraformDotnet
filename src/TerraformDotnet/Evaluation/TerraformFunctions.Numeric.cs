using System.Numerics;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    private static void AddNumeric(Dictionary<string, Entry> table)
    {
        Add(table, "abs", 1, 1, c => Num(Math.Abs(c.Number(0))));
        Add(table, "ceil", 1, 1, c => Num(Math.Ceiling(c.Number(0))));
        Add(table, "floor", 1, 1, c => Num(Math.Floor(c.Number(0))));
        Add(table, "signum", 1, 1, c => Num(Math.Sign(c.Number(0))));
        Add(table, "pow", 2, 2, Pow);
        Add(table, "log", 2, 2, Log);
        Add(table, "parseint", 2, 2, ParseInt);
        Add(table, "max", 1, -1, c => Num(c.Values.Select((v, i) => c.Number(i)).Max()));
        Add(table, "min", 1, -1, c => Num(c.Values.Select((v, i) => c.Number(i)).Min()));
        Add(table, "sum", 1, 1, Sum);
    }

    private static HclValue Pow(FunctionCall c)
    {
        var result = Math.Pow(c.Number(0), c.Number(1));

        return double.IsFinite(result) ? Num(result) : throw c.Error("the result is not a finite number.");
    }

    private static HclValue Log(FunctionCall c)
    {
        var value = c.Number(0);
        var logBase = c.Number(1);
        if (value <= 0 || logBase <= 0)
        {
            throw c.Error("the logarithm is only defined for positive numbers and bases.");
        }

        var result = Math.Log(value) / Math.Log(logBase);

        return double.IsFinite(result) ? Num(result) : throw c.Error("the result is not a finite number.");
    }

    private static HclValue Sum(FunctionCall c)
    {
        var items = c.List(0);
        if (items.Count == 0)
        {
            throw c.Error("cannot sum an empty list.");
        }

        var total = 0d;
        foreach (var item in items)
        {
            total += c.AsNumber(item, "list element");
        }

        return Num(total);
    }

    private static HclValue ParseInt(FunctionCall c)
    {
        var text = c.StrictString(0);
        var radix = c.Int(1);
        if (radix is < 2 or > 62)
        {
            throw c.Error("base must be a whole number between 2 and 62 inclusive.");
        }

        var negative = false;
        var start = 0;
        if (text.Length > 0 && text[0] is '-' or '+')
        {
            negative = text[0] == '-';
            start = 1;
        }

        if (start >= text.Length)
        {
            throw c.Error("cannot parse an empty string as an integer.");
        }

        BigInteger value = BigInteger.Zero;
        for (var i = start; i < text.Length; i++)
        {
            var digit = DigitValue(text[i], radix);
            if (digit < 0)
            {
                throw c.Error($"cannot parse \"{text}\" as a base {radix} integer.");
            }

            value = (value * radix) + digit;
        }

        return Num((double)(negative ? -value : value));
    }

    /// <summary>Digit value like Go's <c>big.Int.SetString</c>: case-insensitive up to base 36, then a-z, A-Z.</summary>
    private static int DigitValue(char ch, int radix)
    {
        int digit;
        if (ch is >= '0' and <= '9')
        {
            digit = ch - '0';
        }
        else if (ch is >= 'a' and <= 'z')
        {
            digit = ch - 'a' + 10;
        }
        else if (ch is >= 'A' and <= 'Z')
        {
            digit = radix <= 36 ? ch - 'A' + 10 : ch - 'A' + 36;
        }
        else
        {
            return -1;
        }

        return digit < radix ? digit : -1;
    }
}
