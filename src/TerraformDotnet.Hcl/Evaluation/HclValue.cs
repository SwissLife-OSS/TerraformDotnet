using System.Collections.ObjectModel;
using System.Globalization;

namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Represents a resolved HCL value produced by the <see cref="HclEvaluator"/>.
/// Uses a tagged-union design: the <see cref="Type"/> property identifies which
/// accessor (<see cref="StringValue"/>, <see cref="NumberValue"/>, etc.) holds
/// the resolved data.
/// </summary>
/// <example>
/// <code>
/// var value = HclValue.FromString("hello");
/// if (value.Type == HclValueType.String)
///     Console.WriteLine(value.StringValue); // "hello"
/// </code>
/// </example>
public sealed class HclValue : IEquatable<HclValue>
{
    /// <summary>The singleton null value.</summary>
    public static readonly HclValue Null = new(HclValueType.Null);

    /// <summary>The singleton <c>true</c> value.</summary>
    public static readonly HclValue True = new(true);

    /// <summary>The singleton <c>false</c> value.</summary>
    public static readonly HclValue False = new(false);

    private readonly string? _stringValue;
    private readonly double _numberValue;
    private readonly bool _boolValue;
    private readonly ReadOnlyCollection<HclValue>? _tupleValue;
    private readonly ReadOnlyDictionary<string, HclValue>? _objectValue;
    private readonly string? _unknownSource;
    private readonly ReadOnlyCollection<HclValue>? _unknownArgs;

    /// <summary>The resolved value type.</summary>
    public HclValueType Type { get; }

    /// <summary>Gets the string value. Only valid when <see cref="Type"/> is <see cref="HclValueType.String"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a string.</exception>
    public string StringValue => Type == HclValueType.String
        ? _stringValue!
        : throw new InvalidOperationException($"Cannot read StringValue from {Type} value.");

    /// <summary>Gets the numeric value. Only valid when <see cref="Type"/> is <see cref="HclValueType.Number"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a number.</exception>
    public double NumberValue => Type == HclValueType.Number
        ? _numberValue
        : throw new InvalidOperationException($"Cannot read NumberValue from {Type} value.");

    /// <summary>Gets the boolean value. Only valid when <see cref="Type"/> is <see cref="HclValueType.Bool"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a boolean.</exception>
    public bool BoolValue => Type == HclValueType.Bool
        ? _boolValue
        : throw new InvalidOperationException($"Cannot read BoolValue from {Type} value.");

    /// <summary>Gets the tuple elements. Only valid when <see cref="Type"/> is <see cref="HclValueType.Tuple"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a tuple.</exception>
    public ReadOnlyCollection<HclValue> TupleValue => Type == HclValueType.Tuple
        ? _tupleValue!
        : throw new InvalidOperationException($"Cannot read TupleValue from {Type} value.");

    /// <summary>Gets the object entries. Only valid when <see cref="Type"/> is <see cref="HclValueType.Object"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not an object.</exception>
    public ReadOnlyDictionary<string, HclValue> ObjectValue => Type == HclValueType.Object
        ? _objectValue!
        : throw new InvalidOperationException($"Cannot read ObjectValue from {Type} value.");

    /// <summary>Gets the source identifier of the unknown value (e.g. function name). Only valid when <see cref="Type"/> is <see cref="HclValueType.Unknown"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not unknown.</exception>
    public string UnknownSource => Type == HclValueType.Unknown
        ? _unknownSource!
        : throw new InvalidOperationException($"Cannot read UnknownSource from {Type} value.");

    /// <summary>Gets the arguments of the unknown value (e.g. function arguments). Only valid when <see cref="Type"/> is <see cref="HclValueType.Unknown"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not unknown.</exception>
    public ReadOnlyCollection<HclValue> UnknownArgs => Type == HclValueType.Unknown
        ? _unknownArgs!
        : throw new InvalidOperationException($"Cannot read UnknownArgs from {Type} value.");

    // Private constructors for each variant

    private HclValue(HclValueType type)
    {
        Type = type;
    }

    private HclValue(string value)
    {
        Type = HclValueType.String;
        _stringValue = value;
    }

    private HclValue(double value)
    {
        Type = HclValueType.Number;
        _numberValue = value;
    }

    private HclValue(bool value)
    {
        Type = HclValueType.Bool;
        _boolValue = value;
    }

    private HclValue(ReadOnlyCollection<HclValue> tuple)
    {
        Type = HclValueType.Tuple;
        _tupleValue = tuple;
    }

    private HclValue(ReadOnlyDictionary<string, HclValue> obj)
    {
        Type = HclValueType.Object;
        _objectValue = obj;
    }

    private HclValue(string source, ReadOnlyCollection<HclValue> args)
    {
        Type = HclValueType.Unknown;
        _unknownSource = source;
        _unknownArgs = args;
    }

    /// <summary>Creates a string value.</summary>
    /// <param name="value">The string content.</param>
    /// <returns>An <see cref="HclValue"/> of type <see cref="HclValueType.String"/>.</returns>
    public static HclValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new HclValue(value);
    }

    /// <summary>Creates a numeric value.</summary>
    /// <param name="value">The numeric content.</param>
    /// <returns>An <see cref="HclValue"/> of type <see cref="HclValueType.Number"/>.</returns>
    public static HclValue FromNumber(double value) => new(value);

    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">The boolean content.</param>
    /// <returns>An <see cref="HclValue"/> singleton for the given boolean.</returns>
    public static HclValue FromBool(bool value) => value ? True : False;

    /// <summary>Creates a tuple (list) value from a list of elements.</summary>
    /// <param name="elements">The ordered elements.</param>
    /// <returns>An <see cref="HclValue"/> of type <see cref="HclValueType.Tuple"/>.</returns>
    public static HclValue FromTuple(IList<HclValue> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        return new HclValue(new ReadOnlyCollection<HclValue>(elements));
    }

    /// <summary>Creates an object (map) value from a dictionary of entries.</summary>
    /// <param name="entries">The string-keyed entries.</param>
    /// <returns>An <see cref="HclValue"/> of type <see cref="HclValueType.Object"/>.</returns>
    public static HclValue FromObject(IDictionary<string, HclValue> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return new HclValue(new ReadOnlyDictionary<string, HclValue>(
            new Dictionary<string, HclValue>(entries)));
    }

    /// <summary>
    /// Creates an unknown value representing an unresolvable expression, such as a function call.
    /// </summary>
    /// <param name="source">A description of why this value is unknown (e.g. function name).</param>
    /// <param name="args">The resolved arguments, if any.</param>
    /// <returns>An <see cref="HclValue"/> of type <see cref="HclValueType.Unknown"/>.</returns>
    public static HclValue Unknown(string source, IList<HclValue>? args = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new HclValue(source, new ReadOnlyCollection<HclValue>(
            args ?? Array.Empty<HclValue>()));
    }

    /// <summary>
    /// Returns <c>true</c> if this value is truthy for conditional evaluation.
    /// </summary>
    /// <remarks>
    /// Only boolean values are considered truthy/falsy. Other types throw
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public bool IsTruthy => Type switch
    {
        HclValueType.Bool => _boolValue,
        _ => throw new InvalidOperationException(
            $"Cannot evaluate truthiness of {Type} value. Only Bool values support conditional evaluation."),
    };

    /// <summary>
    /// Converts this value to its string representation, suitable for template interpolation.
    /// </summary>
    /// <returns>A human-readable string representation of the value.</returns>
    public string ToHclString() => Type switch
    {
        HclValueType.String => _stringValue!,
        HclValueType.Number => _numberValue.ToString(CultureInfo.InvariantCulture),
        HclValueType.Bool => _boolValue ? "true" : "false",
        HclValueType.Null => "null",
        _ => throw new InvalidOperationException(
            $"Cannot convert {Type} value to string for template interpolation."),
    };

    /// <inheritdoc />
    public bool Equals(HclValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Type != other.Type)
        {
            return false;
        }

        return Type switch
        {
            HclValueType.String => _stringValue == other._stringValue,
            HclValueType.Number => _numberValue.Equals(other._numberValue),
            HclValueType.Bool => _boolValue == other._boolValue,
            HclValueType.Null => true,
            HclValueType.Tuple => TupleElementsEqual(other),
            HclValueType.Object => ObjectEntriesEqual(other),
            HclValueType.Unknown => _unknownSource == other._unknownSource,
            _ => false,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as HclValue);

    /// <inheritdoc />
    public override int GetHashCode() => Type switch
    {
        HclValueType.String => HashCode.Combine(Type, _stringValue),
        HclValueType.Number => HashCode.Combine(Type, _numberValue),
        HclValueType.Bool => HashCode.Combine(Type, _boolValue),
        HclValueType.Null => HashCode.Combine(Type),
        HclValueType.Tuple => HashCode.Combine(Type, _tupleValue!.Count),
        HclValueType.Object => HashCode.Combine(Type, _objectValue!.Count),
        HclValueType.Unknown => HashCode.Combine(Type, _unknownSource),
        _ => 0,
    };

    /// <inheritdoc />
    public override string ToString() => Type switch
    {
        HclValueType.String => $"\"{_stringValue}\"",
        HclValueType.Number => _numberValue.ToString(CultureInfo.InvariantCulture),
        HclValueType.Bool => _boolValue ? "true" : "false",
        HclValueType.Null => "null",
        HclValueType.Tuple => $"[{string.Join(", ", _tupleValue!)}]",
        HclValueType.Object => $"{{{string.Join(", ", _objectValue!.Select(kv => $"{kv.Key} = {kv.Value}"))}}}",
        HclValueType.Unknown => $"unknown({_unknownSource})",
        _ => "?",
    };

    /// <summary>Equality operator.</summary>
    public static bool operator ==(HclValue? left, HclValue? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(HclValue? left, HclValue? right) => !(left == right);

    private bool TupleElementsEqual(HclValue other)
    {
        if (_tupleValue!.Count != other._tupleValue!.Count)
        {
            return false;
        }

        for (int i = 0; i < _tupleValue.Count; i++)
        {
            if (!_tupleValue[i].Equals(other._tupleValue[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool ObjectEntriesEqual(HclValue other)
    {
        if (_objectValue!.Count != other._objectValue!.Count)
        {
            return false;
        }

        foreach (var kvp in _objectValue)
        {
            if (!other._objectValue.TryGetValue(kvp.Key, out var otherVal) || !kvp.Value.Equals(otherVal))
            {
                return false;
            }
        }

        return true;
    }
}
