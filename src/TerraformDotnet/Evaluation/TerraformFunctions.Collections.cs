using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    private const int MaxRangeLength = 1024;

    private static void AddCollections(Dictionary<string, Entry> table)
    {
        Add(table, "length", 1, 1, Length);
        Add(table, "keys", 1, 1, c => List(SortedKeys(c.Map(0)).Select(Str).ToList()));
        Add(table, "values", 1, 1, c => List(SortedKeys(c.Map(0)).Select(k => c.Map(0)[k]).ToList()));
        Add(table, "lookup", 2, 3, Lookup);
        Add(table, "element", 2, 2, Element);
        Add(table, "index", 2, 2, IndexOf);
        Add(table, "contains", 2, 2, c => Bool(c.List(0).Contains(c.NonNull(1))));
        Add(table, "concat", 1, -1, Concat);
        Add(table, "flatten", 1, 1, c => List(Flatten(c.List(0))));
        Add(table, "chunklist", 2, 2, ChunkList);
        Add(table, "distinct", 1, 1, Distinct);
        Add(table, "compact", 1, 1, Compact);
        Add(table, "coalesce", 1, -1, Coalesce);
        Add(table, "coalescelist", 1, -1, CoalesceList);
        Add(table, "merge", 0, -1, Merge);
        Add(table, "one", 1, 1, One);
        Add(table, "range", 1, 3, Range);
        Add(table, "reverse", 1, 1, c => List(c.List(0).Reverse().ToList()));
        Add(table, "slice", 3, 3, Slice);
        Add(table, "sort", 1, 1, Sort);
        Add(table, "setunion", 1, -1, c => SetOperation(c, SetOp.Union));
        Add(table, "setintersection", 1, -1, c => SetOperation(c, SetOp.Intersection));
        Add(table, "setsubtract", 2, 2, c => SetOperation(c, SetOp.Subtract));
        Add(table, "setproduct", 2, -1, SetProduct);
        Add(table, "matchkeys", 3, 3, MatchKeys);
        Add(table, "transpose", 1, 1, Transpose);
        Add(table, "zipmap", 2, 2, ZipMap);
        Add(table, "alltrue", 1, 1, c => Bool(c.List(0).Select(e => TruthOf(c, e)).ToList().All(t => t)));
        Add(table, "anytrue", 1, 1, c => Bool(c.List(0).Select(e => TruthOf(c, e)).ToList().Any(t => t)));
    }

    private static List<string> SortedKeys(IReadOnlyDictionary<string, HclValue> map)
    {
        var keys = map.Keys.ToList();
        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    private static HclValue Length(FunctionCall c)
    {
        var value = c.NonNull(0);

        return value.Type switch
        {
            HclValueType.String => Num(Graphemes(value.StringValue).Count),
            HclValueType.Tuple => Num(value.TupleValue.Count),
            HclValueType.Object => Num(value.ObjectValue.Count),
            _ => throw c.Error($"argument must be a string, list, set, map or object, not {FunctionCall.Describe(value)}."),
        };
    }

    private static HclValue Lookup(FunctionCall c)
    {
        var map = c.Map(0);
        var key = c.StrictString(1);
        if (map.TryGetValue(key, out var value))
        {
            return value;
        }

        return c.Count == 3 ? c.Any(2) : throw c.Error($"the given key \"{key}\" does not exist in the given map.");
    }

    private static HclValue Element(FunctionCall c)
    {
        var items = c.List(0);
        var index = c.Int(1);
        if (index < 0)
        {
            throw c.Error("the index must not be negative.");
        }

        if (items.Count == 0)
        {
            throw c.Error("cannot use element on an empty list.");
        }

        return items[index % items.Count];
    }

    private static HclValue IndexOf(FunctionCall c)
    {
        var items = c.List(0);
        var wanted = c.NonNull(1);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Equals(wanted))
            {
                return Num(i);
            }
        }

        throw c.Error("the given value is not present in the list.");
    }

    private static HclValue Concat(FunctionCall c)
    {
        var result = new List<HclValue>();
        for (var i = 0; i < c.Count; i++)
        {
            result.AddRange(c.List(i));
        }

        return List(result);
    }

    private static List<HclValue> Flatten(IReadOnlyList<HclValue> items)
    {
        var result = new List<HclValue>();
        foreach (var item in items)
        {
            if (item.Type == HclValueType.Tuple)
            {
                result.AddRange(Flatten(item.TupleValue));
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static HclValue ChunkList(FunctionCall c)
    {
        var items = c.List(0);
        var size = c.Int(1);
        if (size < 0)
        {
            throw c.Error("the chunk size must not be negative.");
        }

        if (items.Count == 0)
        {
            return List(new List<HclValue>());
        }

        if (size == 0)
        {
            return List(new List<HclValue> { List(items.ToList()) });
        }

        return List(items.Chunk(size).Select(chunk => List(chunk.ToList())).ToList());
    }

    private static HclValue Distinct(FunctionCall c)
    {
        var result = new List<HclValue>();
        foreach (var item in TerraformValues.UnifyPrimitives(c.List(0)))
        {
            if (!result.Contains(item))
            {
                result.Add(item);
            }
        }

        return List(result);
    }

    private static HclValue Compact(FunctionCall c)
    {
        var result = new List<HclValue>();
        foreach (var item in c.List(0))
        {
            if (item.Type == HclValueType.Null)
            {
                continue;
            }

            var text = c.AsString(item, "list element");
            if (text.Length > 0)
            {
                result.Add(Str(text));
            }
        }

        return List(result);
    }

    private static HclValue Coalesce(FunctionCall c)
    {
        var unify = TerraformValues.CreateUnifier(c.Values);
        foreach (var value in c.Values)
        {
            if (value.Type == HclValueType.Null || (value.Type == HclValueType.String && value.StringValue.Length == 0))
            {
                continue;
            }

            return unify(value);
        }

        throw c.Error("no non-null, non-empty-string arguments.");
    }

    private static HclValue CoalesceList(FunctionCall c)
    {
        for (var i = 0; i < c.Count; i++)
        {
            var items = c.List(i);
            if (items.Count > 0)
            {
                return c.Values[i];
            }
        }

        throw c.Error("no non-null, non-empty-list arguments.");
    }

    private static HclValue Merge(FunctionCall c)
    {
        var result = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        foreach (var argument in c.Values)
        {
            if (argument.Type == HclValueType.Null)
            {
                continue;
            }

            if (argument.Type != HclValueType.Object)
            {
                throw c.Error($"arguments must be maps or objects, not {FunctionCall.Describe(argument)}.");
            }

            foreach (var entry in argument.ObjectValue)
            {
                result[entry.Key] = entry.Value;
            }
        }

        return HclValue.FromObject(result);
    }

    private static HclValue One(FunctionCall c)
    {
        var items = c.List(0);

        return items.Count switch
        {
            0 => HclValue.Null,
            1 => items[0],
            _ => throw c.Error("must be a list, set or tuple value with either zero or one elements."),
        };
    }

    private static HclValue Range(FunctionCall c)
    {
        var start = c.Count > 1 ? c.Number(0) : 0;
        var limit = c.Count > 1 ? c.Number(1) : c.Number(0);
        var step = c.Count == 3 ? c.Number(2) : (limit < start ? -1 : 1);
        if (step == 0)
        {
            throw c.Error("the step must not be zero.");
        }

        if (c.Count == 3 && (step > 0 ? limit < start : limit > start))
        {
            throw c.Error("the step points away from the limit.");
        }

        var result = new List<HclValue>();
        for (var value = start; step > 0 ? value < limit : value > limit; value += step)
        {
            if (result.Count >= MaxRangeLength)
            {
                throw c.Error($"more than {MaxRangeLength} list elements.");
            }

            result.Add(Num(value));
        }

        return List(result);
    }

    private static HclValue Slice(FunctionCall c)
    {
        var items = c.List(0);
        var start = c.Int(1);
        var end = c.Int(2);
        if (start < 0 || end < start || end > items.Count)
        {
            throw c.Error("the start and end indices are out of range.");
        }

        return List(items.Skip(start).Take(end - start).ToList());
    }

    private static HclValue Sort(FunctionCall c)
    {
        var texts = c.List(0).Select(item => c.AsString(item, "list element")).ToList();
        texts.Sort(StringComparer.Ordinal);

        return List(texts.Select(Str).ToList());
    }

    private enum SetOp
    {
        Union,
        Intersection,
        Subtract,
    }

    private static HclValue SetOperation(FunctionCall c, SetOp operation)
    {
        var sets = new List<IReadOnlyList<HclValue>>();
        for (var i = 0; i < c.Count; i++)
        {
            sets.Add(c.List(i));
        }

        var unify = TerraformValues.CreateUnifier(sets.SelectMany(s => s));
        var normalized = sets.Select(s => s.Select(unify).ToList()).ToList();

        IEnumerable<HclValue> result = operation switch
        {
            SetOp.Union => normalized.SelectMany(s => s),
            SetOp.Intersection => normalized[0].Where(e => normalized.Skip(1).All(s => s.Contains(e))),
            _ => normalized[0].Where(e => !normalized[1].Contains(e)),
        };

        return TerraformValues.ToSet(result);
    }

    private static HclValue SetProduct(FunctionCall c)
    {
        var lists = new List<IReadOnlyList<HclValue>>();
        for (var i = 0; i < c.Count; i++)
        {
            lists.Add(c.List(i));
        }

        var combinations = new List<List<HclValue>> { new() };
        foreach (var list in lists)
        {
            combinations = combinations
                .SelectMany(existing => list.Select(element => new List<HclValue>(existing) { element }))
                .ToList();
            if (combinations.Count > 100_000)
            {
                throw c.Error("the product is too large to evaluate.");
            }
        }

        return List(combinations.Select(combination => List(combination)).ToList());
    }

    private static HclValue MatchKeys(FunctionCall c)
    {
        var values = c.List(0);
        var keys = c.List(1);
        var search = c.List(2);
        if (values.Count != keys.Count)
        {
            throw c.Error("the lists of values and keys must have equal lengths.");
        }

        var result = new List<HclValue>();
        for (var i = 0; i < values.Count; i++)
        {
            if (search.Contains(keys[i]))
            {
                result.Add(values[i]);
            }
        }

        return List(result);
    }

    private static HclValue Transpose(FunctionCall c)
    {
        var map = c.Map(0);
        var result = new Dictionary<string, List<HclValue>>(StringComparer.Ordinal);
        foreach (var key in SortedKeys(map))
        {
            if (map[key].Type != HclValueType.Tuple)
            {
                throw c.Error("the map values must be lists of strings.");
            }

            foreach (var item in map[key].TupleValue)
            {
                var text = c.AsString(item, "list element");
                if (!result.TryGetValue(text, out var owners))
                {
                    owners = new List<HclValue>();
                    result[text] = owners;
                }

                owners.Add(Str(key));
            }
        }

        return HclValue.FromObject(result.ToDictionary(e => e.Key, e => List(e.Value), StringComparer.Ordinal));
    }

    private static HclValue ZipMap(FunctionCall c)
    {
        var keys = c.List(0);
        var values = c.List(1);
        if (keys.Count != values.Count)
        {
            throw c.Error($"the key and value lists must have equal lengths ({keys.Count} and {values.Count}).");
        }

        var result = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            result[c.AsString(keys[i], "key")] = values[i];
        }

        return HclValue.FromObject(result);
    }

    private static bool TruthOf(FunctionCall c, HclValue element)
        => element.Type != HclValueType.Null && c.AsBool(element, "list element");
}
