using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Validation;

/// <summary>
/// Small structural helpers shared by the constraint recognisers and the predicate builder.
/// </summary>
internal static class ExpressionAnalysis
{
    private const int MaxLocalDepth = 8;

    /// <summary>Matches <c>var.name</c> (and <c>var["name"]</c>).</summary>
    public static bool TryGetVariableName(HclExpression expression, out string name)
    {
        switch (expression)
        {
            case HclAttributeAccessExpression { Source: HclVariableExpression { Name: "var" } } access:
                name = access.Name;

                return true;

            case HclIndexExpression
            {
                Collection: HclVariableExpression { Name: "var" },
                Index: HclLiteralExpression { Kind: HclLiteralKind.String, Value: { } key },
            }:
                name = key;

                return true;

            default:
                name = string.Empty;

                return false;
        }
    }

    /// <summary>Collects the names of all <c>var.*</c> references inside an expression.</summary>
    public static void CollectVariableReferences(HclExpression expression, ICollection<string> names)
    {
        if (TryGetVariableName(expression, out var name))
        {
            names.Add(name);

            return;
        }

        switch (expression)
        {
            case HclBinaryExpression binary:
                CollectVariableReferences(binary.Left, names);
                CollectVariableReferences(binary.Right, names);
                break;

            case HclUnaryExpression unary:
                CollectVariableReferences(unary.Operand, names);
                break;

            case HclConditionalExpression conditional:
                CollectVariableReferences(conditional.Condition, names);
                CollectVariableReferences(conditional.TrueResult, names);
                CollectVariableReferences(conditional.FalseResult, names);
                break;

            case HclFunctionCallExpression function:
                foreach (var argument in function.Arguments)
                {
                    CollectVariableReferences(argument, names);
                }

                break;

            case HclTupleExpression tuple:
                foreach (var element in tuple.Elements)
                {
                    CollectVariableReferences(element, names);
                }

                break;

            case HclObjectExpression obj:
                foreach (var element in obj.Elements)
                {
                    CollectVariableReferences(element.Key, names);
                    CollectVariableReferences(element.Value, names);
                }

                break;

            case HclForExpression forExpression:
                CollectVariableReferences(forExpression.Collection, names);
                CollectVariableReferences(forExpression.ValueExpression, names);

                if (forExpression.KeyExpression is not null)
                {
                    CollectVariableReferences(forExpression.KeyExpression, names);
                }

                if (forExpression.Condition is not null)
                {
                    CollectVariableReferences(forExpression.Condition, names);
                }

                break;

            case HclIndexExpression index:
                CollectVariableReferences(index.Collection, names);
                CollectVariableReferences(index.Index, names);
                break;

            case HclAttributeAccessExpression access:
                CollectVariableReferences(access.Source, names);
                break;

            case HclSplatExpression splat:
                CollectVariableReferences(splat.Source, names);

                foreach (var step in splat.Traversal)
                {
                    CollectVariableReferences(step, names);
                }

                break;

            case HclTemplateExpression template:
                foreach (var part in template.Parts)
                {
                    CollectVariableReferences(part, names);
                }

                break;

            case HclTemplateWrapExpression wrap:
                CollectVariableReferences(wrap.Wrapped, names);
                break;
        }
    }

    /// <summary>Splits a tree of <c>&amp;&amp;</c> into its operands, left to right.</summary>
    public static List<HclExpression> FlattenAnd(HclExpression expression) =>
        Flatten(expression, HclBinaryOperator.And);

    /// <summary>Splits a tree of <c>||</c> into its operands, left to right.</summary>
    public static List<HclExpression> FlattenOr(HclExpression expression) =>
        Flatten(expression, HclBinaryOperator.Or);

    /// <summary>Joins operands with a left-associative binary operator.</summary>
    public static HclExpression BuildBinary(HclBinaryOperator op, IReadOnlyList<HclExpression> operands)
    {
        var result = operands[0];

        for (var i = 1; i < operands.Count; i++)
        {
            result = new HclBinaryExpression
            {
                Left = result,
                Operator = op,
                Right = operands[i],
            };
        }

        return result;
    }

    public static bool IsBoolLiteral(HclExpression expression, bool value) =>
        expression is HclLiteralExpression { Kind: HclLiteralKind.Bool } literal
        && string.Equals(literal.Value, value ? "true" : "false", StringComparison.OrdinalIgnoreCase);

    public static bool IsNullLiteral(HclExpression expression) =>
        expression is HclLiteralExpression { Kind: HclLiteralKind.Null };

    /// <summary>Matches a call to <paramref name="name"/> with exactly <paramref name="argumentCount"/> arguments.</summary>
    public static bool TryGetFunction(
        HclExpression expression,
        string name,
        int argumentCount,
        out IReadOnlyList<HclExpression> arguments)
    {
        if (expression is HclFunctionCallExpression function
            && !function.ExpandFinalArgument
            && function.Name == name
            && function.Arguments.Count == argumentCount)
        {
            arguments = function.Arguments;

            return true;
        }

        arguments = [];

        return false;
    }

    public static bool TryGetString(HclExpression expression, out string value)
    {
        if (expression is HclLiteralExpression { Kind: HclLiteralKind.String, Value: { } text })
        {
            value = text;

            return true;
        }

        value = string.Empty;

        return false;
    }

    public static bool TryGetNumber(HclExpression expression, out decimal value)
    {
        if (TerraformLiteral.TryFromExpression(expression, out var literal) && literal.TryGetNumber(out value))
        {
            return true;
        }

        value = 0;

        return false;
    }

    public static bool TryGetInteger(HclExpression expression, out int value)
    {
        if (TryGetNumber(expression, out var number)
            && number == decimal.Truncate(number)
            && number >= int.MinValue
            && number <= int.MaxValue)
        {
            value = (int)number;

            return true;
        }

        value = 0;

        return false;
    }

    /// <summary>Follows <c>local.name</c> references to their defining expression.</summary>
    public static HclExpression ResolveLocal(HclExpression expression, IReadOnlyDictionary<string, HclExpression> locals)
    {
        for (var depth = 0; depth < MaxLocalDepth; depth++)
        {
            if (expression is HclAttributeAccessExpression { Source: HclVariableExpression { Name: "local" } } access
                && locals.TryGetValue(access.Name, out var resolved))
            {
                expression = resolved;

                continue;
            }

            break;
        }

        return expression;
    }

    /// <summary>
    /// Resolves an expression to a non-empty list of literals. Understands tuples of literals,
    /// <c>local.*</c> references to them, <c>toset/tolist/distinct(...)</c>, and <c>keys(...)</c>/<c>values(...)</c>
    /// of object literals.
    /// </summary>
    public static bool TryResolveLiteralList(
        HclExpression expression,
        IReadOnlyDictionary<string, HclExpression> locals,
        out IReadOnlyList<TerraformLiteral> values)
    {
        values = [];
        expression = ResolveLocal(expression, locals);

        switch (expression)
        {
            case HclTupleExpression tuple:
                var literals = new List<TerraformLiteral>(tuple.Elements.Count);

                foreach (var element in tuple.Elements)
                {
                    if (!TerraformLiteral.TryFromExpression(element, out var literal) || literal.IsNull)
                    {
                        return false;
                    }

                    literals.Add(literal);
                }

                values = literals;

                return literals.Count > 0;

            case HclFunctionCallExpression { Name: "toset" or "tolist" or "distinct", Arguments.Count: 1 } wrapper:
                return TryResolveLiteralList(wrapper.Arguments[0], locals, out values);

            case HclFunctionCallExpression { Name: "keys", Arguments.Count: 1 } keys
                when ResolveLocal(keys.Arguments[0], locals) is HclObjectExpression keyed:
                var names = new List<TerraformLiteral>(keyed.Elements.Count);

                foreach (var element in keyed.Elements)
                {
                    if (element.ForceKey || !TryGetObjectKey(element.Key, out var key))
                    {
                        return false;
                    }

                    names.Add(TerraformLiteral.FromString(key));
                }

                values = names;

                return names.Count > 0;

            case HclFunctionCallExpression { Name: "values", Arguments.Count: 1 } valuesCall
                when ResolveLocal(valuesCall.Arguments[0], locals) is HclObjectExpression valued:
                var items = new List<TerraformLiteral>(valued.Elements.Count);

                foreach (var element in valued.Elements)
                {
                    if (!TerraformLiteral.TryFromExpression(element.Value, out var item) || item.IsNull)
                    {
                        return false;
                    }

                    items.Add(item);
                }

                values = items;

                return items.Count > 0;

            default:
                return false;
        }
    }

    private static bool TryGetObjectKey(HclExpression key, out string name)
    {
        switch (key)
        {
            case HclVariableExpression variable:
                name = variable.Name;

                return true;

            case HclLiteralExpression { Kind: HclLiteralKind.String, Value: { } text }:
                name = text;

                return true;

            case HclLiteralExpression { Kind: HclLiteralKind.Number, Value: { } number }:
                name = number;

                return true;

            default:
                name = string.Empty;

                return false;
        }
    }

    private static List<HclExpression> Flatten(HclExpression expression, HclBinaryOperator op)
    {
        var result = new List<HclExpression>();
        var pending = new Stack<HclExpression>();
        pending.Push(expression);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (current is HclBinaryExpression binary && binary.Operator == op)
            {
                // Push right first so operands come out in source order.
                pending.Push(binary.Right);
                pending.Push(binary.Left);
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }
}
