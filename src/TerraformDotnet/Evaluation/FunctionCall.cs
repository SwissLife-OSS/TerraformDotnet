using System.Globalization;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

/// <summary>
/// The name and evaluated arguments of one function call, with typed accessors that convert
/// like Terraform does and raise <see cref="HclFunctionException"/> for unsuitable values.
/// </summary>
internal readonly struct FunctionCall
{
    public FunctionCall(string name, IReadOnlyList<HclValue> values)
    {
        Name = name;
        Values = values;
    }

    /// <summary>Gets the function name.</summary>
    public string Name { get; }

    /// <summary>Gets the evaluated arguments.</summary>
    public IReadOnlyList<HclValue> Values { get; }

    /// <summary>Gets the number of arguments.</summary>
    public int Count => Values.Count;

    /// <summary>Creates an exception describing an invalid call to this function.</summary>
    public HclFunctionException Error(string message) => new(Name, message);

    /// <summary>Gets an argument without any conversion; may be null-typed.</summary>
    public HclValue Any(int index) => Values[index];

    /// <summary>Gets an argument that must not be null.</summary>
    public HclValue NonNull(int index) => Values[index].Type == HclValueType.Null
        ? throw Error($"argument {index + 1} must not be null.")
        : Values[index];

    /// <summary>Gets a string argument; numbers and booleans are converted.</summary>
    public string String(int index) => AsString(NonNull(index), $"argument {index + 1}");

    /// <summary>Gets a string argument that must be an actual string (no conversion).</summary>
    public string StrictString(int index)
    {
        var value = NonNull(index);

        return value.Type == HclValueType.String
            ? value.StringValue
            : throw Error($"argument {index + 1} must be a string, not a {Describe(value)}.");
    }

    /// <summary>Gets a numeric argument; numeric strings are converted.</summary>
    public double Number(int index) => AsNumber(NonNull(index), $"argument {index + 1}");

    /// <summary>Gets an integral numeric argument that fits in an <see cref="int"/>.</summary>
    public int Int(int index) => AsInt(NonNull(index), $"argument {index + 1}");

    /// <summary>Gets a list, set or tuple argument.</summary>
    public IReadOnlyList<HclValue> List(int index)
    {
        var value = NonNull(index);

        return value.Type == HclValueType.Tuple
            ? value.TupleValue
            : throw Error($"argument {index + 1} must be a list, set or tuple, not a {Describe(value)}.");
    }

    /// <summary>Gets a map or object argument.</summary>
    public IReadOnlyDictionary<string, HclValue> Map(int index)
    {
        var value = NonNull(index);

        return value.Type == HclValueType.Object
            ? value.ObjectValue
            : throw Error($"argument {index + 1} must be a map or object, not a {Describe(value)}.");
    }

    /// <summary>Converts a value to a string; only numbers and booleans convert implicitly.</summary>
    public string AsString(HclValue value, string what) => value.Type switch
    {
        HclValueType.String or HclValueType.Number or HclValueType.Bool => value.ToHclString(),
        _ => throw Error($"{what} must be a string, not {Describe(value)}."),
    };

    /// <summary>Converts a value to a number; numeric strings convert implicitly.</summary>
    public double AsNumber(HclValue value, string what)
    {
        if (value.Type == HclValueType.Number)
        {
            return value.NumberValue;
        }

        if (value.Type == HclValueType.String && TerraformValues.TryParseNumber(value.StringValue, out var parsed))
        {
            return parsed;
        }

        throw Error($"{what} must be a number, not {Describe(value)}.");
    }

    /// <summary>Converts a value to an <see cref="int"/>; fractional numbers are rejected.</summary>
    public int AsInt(HclValue value, string what)
    {
        var number = AsNumber(value, what);
        if (number != Math.Floor(number) || number < int.MinValue || number > int.MaxValue)
        {
            throw Error($"{what} must be a whole number, not {number.ToString(CultureInfo.InvariantCulture)}.");
        }

        return (int)number;
    }

    /// <summary>Converts a value to a boolean; the strings <c>"true"</c>/<c>"1"</c> and <c>"false"</c>/<c>"0"</c> convert implicitly.</summary>
    public bool AsBool(HclValue value, string what) => value.Type switch
    {
        HclValueType.Bool => value.BoolValue,
        HclValueType.String when value.StringValue is "true" or "1" => true,
        HclValueType.String when value.StringValue is "false" or "0" => false,
        _ => throw Error($"{what} must be a boolean, not {Describe(value)}."),
    };

    /// <summary>Returns a human readable type name for error messages.</summary>
    public static string Describe(HclValue value) => value.Type switch
    {
        HclValueType.Tuple => "a list or tuple",
        HclValueType.Object => "a map or object",
        HclValueType.Null => "null",
        _ => $"a {value.Type.ToString().ToLowerInvariant()}",
    };
}
