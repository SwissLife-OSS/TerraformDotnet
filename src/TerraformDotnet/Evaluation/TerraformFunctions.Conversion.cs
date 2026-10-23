using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    private static void AddConversions(Dictionary<string, Entry> table)
    {
        Add(table, "tostring", 1, 1, ToStringFunction);
        Add(table, "tonumber", 1, 1, ToNumberFunction);
        Add(table, "tobool", 1, 1, ToBoolFunction);
        Add(table, "tolist", 1, 1, ToListFunction);
        Add(table, "toset", 1, 1, ToSetFunction);
        Add(table, "tomap", 1, 1, ToMapFunction);
        Add(table, "sensitive", 1, 1, c => c.Any(0));
        Add(table, "nonsensitive", 1, 1, c => c.Any(0));
    }

    private static HclValue ToStringFunction(FunctionCall c)
    {
        var value = c.Any(0);

        return value.Type == HclValueType.Null ? value : Str(c.AsString(value, "argument"));
    }

    private static HclValue ToNumberFunction(FunctionCall c)
    {
        var value = c.Any(0);
        if (value.Type is HclValueType.Null or HclValueType.Number)
        {
            return value;
        }

        if (value.Type == HclValueType.String && TerraformValues.TryParseNumber(value.StringValue, out var number))
        {
            return Num(number);
        }

        throw c.Error("cannot convert the argument to a number.");
    }

    private static HclValue ToBoolFunction(FunctionCall c)
    {
        var value = c.Any(0);

        return value.Type switch
        {
            HclValueType.Null or HclValueType.Bool => value,
            HclValueType.String when value.StringValue is "true" or "1" => Bool(true),
            HclValueType.String when value.StringValue is "false" or "0" => Bool(false),
            _ => throw c.Error("cannot convert the argument to a bool."),
        };
    }

    private static HclValue ToListFunction(FunctionCall c)
    {
        var value = c.Any(0);

        return value.Type switch
        {
            HclValueType.Null => value,
            HclValueType.Tuple => List(TerraformValues.UnifyPrimitives(value.TupleValue).ToList()),
            _ => throw c.Error($"cannot convert {FunctionCall.Describe(value)} to a list."),
        };
    }

    private static HclValue ToSetFunction(FunctionCall c)
    {
        var value = c.Any(0);

        return value.Type switch
        {
            HclValueType.Null => value,
            HclValueType.Tuple => TerraformValues.ToSet(TerraformValues.UnifyPrimitives(value.TupleValue)),
            _ => throw c.Error($"cannot convert {FunctionCall.Describe(value)} to a set."),
        };
    }

    private static HclValue ToMapFunction(FunctionCall c)
    {
        var value = c.Any(0);
        if (value.Type == HclValueType.Null)
        {
            return value;
        }

        if (value.Type != HclValueType.Object)
        {
            throw c.Error($"cannot convert {FunctionCall.Describe(value)} to a map.");
        }

        var keys = value.ObjectValue.Keys.ToList();
        var unify = TerraformValues.CreateUnifier(keys.Select(k => value.ObjectValue[k]));

        return HclValue.FromObject(keys.ToDictionary(k => k, k => unify(value.ObjectValue[k]), StringComparer.Ordinal));
    }
}
