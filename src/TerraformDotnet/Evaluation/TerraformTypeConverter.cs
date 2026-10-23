using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Types;

namespace TerraformDotnet.Evaluation;

/// <summary>
/// Converts a value to a Terraform variable type constraint the way Terraform does before it
/// runs <c>validation</c> blocks.
/// </summary>
/// <remarks>
/// <para>
/// Primitives convert as in Terraform (<c>"1"</c> to <c>1</c>, <c>true</c> to <c>"true"</c>);
/// tuples convert to <c>list</c>, <c>set</c> (canonical, de-duplicated order) and <c>tuple</c>; objects
/// convert to <c>map</c> and <c>object</c>. For object types, attributes that are not declared are
/// dropped and missing or <c>null</c> <c>optional(...)</c> attributes receive their default (or
/// <c>null</c>). <c>null</c> and unknown values pass through unchanged, as does the <c>any</c> type.
/// </para>
/// </remarks>
public static class TerraformTypeConverter
{
    private static readonly HclEvaluator DefaultEvaluator = new(new HclEvaluatorOptions
    {
        FunctionResolver = TerraformFunctions.Default,
    });

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="type"/>.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="type">The variable type constraint, or <c>null</c> for an implicit <c>any</c>.</param>
    /// <param name="result">The converted value when the conversion succeeds; otherwise the original value.</param>
    /// <param name="error">A description of the first mismatch when the conversion fails; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when the value conforms to the type.</returns>
    public static bool TryConvert(HclValue value, TerraformType? type, out HclValue result, out string? error)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            result = Convert(value, type, string.Empty);
            error = null;

            return true;
        }
        catch (ConversionException exception)
        {
            result = value;
            error = exception.Message;

            return false;
        }
    }

    private static HclValue Convert(HclValue value, TerraformType? type, string path)
    {
        if (type is null || type.Kind == TerraformTypeKind.Any || value.Type is HclValueType.Null or HclValueType.Unknown)
        {
            return value;
        }

        return type switch
        {
            TerraformCollectionType { Kind: TerraformTypeKind.List } list => ConvertList(value, list, path),
            TerraformCollectionType { Kind: TerraformTypeKind.Set } set => ConvertSet(value, set, path),
            TerraformCollectionType map => ConvertMap(value, map, path),
            TerraformObjectType obj => ConvertObject(value, obj, path),
            TerraformTupleType tuple => ConvertTuple(value, tuple, path),
            _ => ConvertPrimitive(value, type.Kind, path),
        };
    }

    private static HclValue ConvertPrimitive(HclValue value, TerraformTypeKind kind, string path)
    {
        switch (kind)
        {
            case TerraformTypeKind.String when value.Type is HclValueType.String:
            case TerraformTypeKind.Number when value.Type is HclValueType.Number:
            case TerraformTypeKind.Bool when value.Type is HclValueType.Bool:
                return value;
            case TerraformTypeKind.String when value.Type is HclValueType.Number or HclValueType.Bool:
                return HclValue.FromString(value.ToHclString());
            case TerraformTypeKind.Number when value.Type is HclValueType.String
                && TerraformValues.TryParseNumber(value.StringValue, out var number):
                return HclValue.FromNumber(number);
            case TerraformTypeKind.Bool when value.Type is HclValueType.String && value.StringValue is "true" or "1":
                return HclValue.True;
            case TerraformTypeKind.Bool when value.Type is HclValueType.String && value.StringValue is "false" or "0":
                return HclValue.False;
            default:
                throw Mismatch(path, kind.ToString().ToLowerInvariant(), value);
        }
    }

    private static HclValue ConvertList(HclValue value, TerraformCollectionType type, string path)
    {
        var elements = ConvertElements(value, type, path, "list");

        return HclValue.FromTuple(elements);
    }

    private static HclValue ConvertSet(HclValue value, TerraformCollectionType type, string path)
    {
        var elements = ConvertElements(value, type, path, "set");

        return TerraformValues.ToSet(elements);
    }

    private static List<HclValue> ConvertElements(HclValue value, TerraformCollectionType type, string path, string expected)
    {
        if (value.Type != HclValueType.Tuple)
        {
            throw Mismatch(path, expected, value);
        }

        var converted = new List<HclValue>(value.TupleValue.Count);
        for (var i = 0; i < value.TupleValue.Count; i++)
        {
            converted.Add(Convert(value.TupleValue[i], type.Element, $"{path}[{i}]"));
        }

        return type.Element.Kind == TerraformTypeKind.Any
            ? TerraformValues.UnifyPrimitives(converted).ToList()
            : converted;
    }

    private static HclValue ConvertMap(HclValue value, TerraformCollectionType type, string path)
    {
        if (value.Type != HclValueType.Object)
        {
            throw Mismatch(path, "map", value);
        }

        var converted = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        foreach (var (key, element) in value.ObjectValue)
        {
            converted[key] = Convert(element, type.Element, $"{path}.{key}");
        }

        if (type.Element.Kind == TerraformTypeKind.Any)
        {
            var unify = TerraformValues.CreateUnifier(converted.Values);
            foreach (var key in converted.Keys.ToList())
            {
                converted[key] = unify(converted[key]);
            }
        }

        return HclValue.FromObject(converted);
    }

    private static HclValue ConvertTuple(HclValue value, TerraformTupleType type, string path)
    {
        if (value.Type != HclValueType.Tuple)
        {
            throw Mismatch(path, "tuple", value);
        }

        if (value.TupleValue.Count != type.Elements.Count)
        {
            throw new ConversionException($"{Describe(path)}: a tuple with {type.Elements.Count} elements is required, but the value has {value.TupleValue.Count}.");
        }

        var converted = new List<HclValue>(type.Elements.Count);
        for (var i = 0; i < type.Elements.Count; i++)
        {
            converted.Add(Convert(value.TupleValue[i], type.Elements[i], $"{path}[{i}]"));
        }

        return HclValue.FromTuple(converted);
    }

    private static HclValue ConvertObject(HclValue value, TerraformObjectType type, string path)
    {
        if (value.Type != HclValueType.Object)
        {
            throw Mismatch(path, "object", value);
        }

        var converted = new Dictionary<string, HclValue>(type.Fields.Count, StringComparer.Ordinal);
        foreach (var field in type.Fields)
        {
            var fieldPath = $"{path}.{field.Name}";
            var present = value.ObjectValue.TryGetValue(field.Name, out var supplied);
            if (present && supplied!.Type != HclValueType.Null)
            {
                converted[field.Name] = Convert(supplied, field.Type, fieldPath);
            }
            else if (field.IsOptional)
            {
                converted[field.Name] = field.Default is null
                    ? HclValue.Null
                    : Convert(EvaluateDefault(field.Default, fieldPath), field.Type, fieldPath);
            }
            else if (present)
            {
                converted[field.Name] = HclValue.Null;
            }
            else
            {
                throw new ConversionException($"{Describe(path)}: attribute \"{field.Name}\" is required.");
            }
        }

        return HclValue.FromObject(converted);
    }

    private static HclValue EvaluateDefault(Hcl.Nodes.HclExpression expression, string path)
    {
        try
        {
            return DefaultEvaluator.Evaluate(expression, new HclEvaluationContext());
        }
        catch (Exception exception) when (exception is InvalidOperationException or HclFunctionException or HclUnresolvableException)
        {
            throw new ConversionException($"{Describe(path)}: the optional attribute default could not be evaluated: {exception.Message}");
        }
    }

    private static ConversionException Mismatch(string path, string expected, HclValue actual)
        => new($"{Describe(path)}: a {expected} is required, but the value is {FunctionCall.Describe(actual)}.");

    private static string Describe(string path) => path.Length == 0 ? "value" : $"value{path}";

    private sealed class ConversionException(string message) : Exception(message);
}
