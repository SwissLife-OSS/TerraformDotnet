using System.Globalization;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Walks an <see cref="HclExpression"/> AST and resolves it to an <see cref="HclValue"/>
/// using variable bindings from an <see cref="HclEvaluationContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator supports literal values, variable references, attribute access, index access,
/// binary/unary operations, conditionals, tuple/object constructors, for expressions, splat
/// expressions, and template interpolation.
/// </para>
/// <para>
/// Function calls are NOT evaluated — they return <see cref="HclValue.Unknown(string, IList{HclValue}?)"/>
/// with the function name and resolved arguments preserved.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var ctx = new HclEvaluationContext();
/// ctx.SetVariable("name", HclValue.FromString("world"));
///
/// var file = HclFile.Load("greeting = \"hello ${name}\""u8);
/// var attr = file.Body.Attributes[0];
///
/// var evaluator = new HclEvaluator();
/// var result = evaluator.Evaluate(attr.Value, ctx);
/// // result.StringValue == "hello world"
/// </code>
/// </example>
public sealed class HclEvaluator
{
    /// <summary>Maximum recursion depth to prevent stack overflow on deeply nested expressions.</summary>
    private const int MaxDepth = 128;

    private int _depth;

    /// <summary>
    /// Evaluates an HCL expression to a resolved value using the given evaluation context.
    /// </summary>
    /// <param name="expression">The expression AST node to evaluate.</param>
    /// <param name="context">The variable bindings available during evaluation.</param>
    /// <returns>The resolved <see cref="HclValue"/>.</returns>
    /// <exception cref="HclUnresolvableException">Thrown when a referenced variable is not found.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an expression type is not supported or evaluation exceeds maximum depth.</exception>
    public HclValue Evaluate(HclExpression expression, HclEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(context);

        _depth = 0;

        return EvaluateExpression(expression, context);
    }

    /// <summary>Recursively evaluates any expression node.</summary>
    private HclValue EvaluateExpression(HclExpression expression, HclEvaluationContext context)
    {
        _depth++;
        if (_depth > MaxDepth)
        {
            throw new InvalidOperationException(
                $"Expression evaluation exceeded maximum recursion depth of {MaxDepth}.");
        }

        try
        {
            return expression switch
            {
                HclLiteralExpression literal => EvaluateLiteral(literal),
                HclVariableExpression variable => EvaluateVariable(variable, context),
                HclBinaryExpression binary => EvaluateBinary(binary, context),
                HclUnaryExpression unary => EvaluateUnary(unary, context),
                HclConditionalExpression conditional => EvaluateConditional(conditional, context),
                HclFunctionCallExpression function => EvaluateFunction(function, context),
                HclTupleExpression tuple => EvaluateTuple(tuple, context),
                HclObjectExpression obj => EvaluateObject(obj, context),
                HclForExpression forExpr => EvaluateFor(forExpr, context),
                HclIndexExpression index => EvaluateIndex(index, context),
                HclAttributeAccessExpression access => EvaluateAttributeAccess(access, context),
                HclSplatExpression splat => EvaluateSplat(splat, context),
                HclTemplateExpression template => EvaluateTemplate(template, context),
                HclTemplateWrapExpression wrap => EvaluateTemplateWrap(wrap, context),
                _ => throw new InvalidOperationException(
                    $"Unsupported expression type: {expression.GetType().Name}"),
            };
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>Resolves a literal expression to a typed value.</summary>
    private static HclValue EvaluateLiteral(HclLiteralExpression literal) => literal.Kind switch
    {
        HclLiteralKind.Null => HclValue.Null,
        HclLiteralKind.Bool => HclValue.FromBool(
            literal.Value is not null &&
            literal.Value.Equals("true", StringComparison.OrdinalIgnoreCase)),
        HclLiteralKind.Number => HclValue.FromNumber(
            double.Parse(literal.Value!, CultureInfo.InvariantCulture)),
        HclLiteralKind.String => HclValue.FromString(literal.Value ?? string.Empty),
        _ => throw new InvalidOperationException($"Unknown literal kind: {literal.Kind}"),
    };

    /// <summary>Looks up a variable reference in the evaluation context.</summary>
    private static HclValue EvaluateVariable(
        HclVariableExpression variable,
        HclEvaluationContext context)
    {
        if (context.TryGetVariable(variable.Name, out var value))
        {
            return value;
        }

        throw new HclUnresolvableException(
            variable.Name,
            $"Variable '{variable.Name}' is not defined in the evaluation context.",
            variable.Start);
    }

    /// <summary>Evaluates a binary operation on two resolved operands.</summary>
    private HclValue EvaluateBinary(HclBinaryExpression binary, HclEvaluationContext context)
    {
        var left = EvaluateExpression(binary.Left, context);
        var right = EvaluateExpression(binary.Right, context);

        // If either side is unknown, the whole expression is unknown
        if (left.Type == HclValueType.Unknown)
        {
            return left;
        }

        if (right.Type == HclValueType.Unknown)
        {
            return right;
        }

        return binary.Operator switch
        {
            HclBinaryOperator.Add => EvaluateArithmetic(left, right, (a, b) => a + b),
            HclBinaryOperator.Subtract => EvaluateArithmetic(left, right, (a, b) => a - b),
            HclBinaryOperator.Multiply => EvaluateArithmetic(left, right, (a, b) => a * b),
            HclBinaryOperator.Divide => EvaluateArithmetic(left, right, (a, b) => a / b),
            HclBinaryOperator.Modulo => EvaluateArithmetic(left, right, (a, b) => a % b),
            HclBinaryOperator.Equal => HclValue.FromBool(left.Equals(right)),
            HclBinaryOperator.NotEqual => HclValue.FromBool(!left.Equals(right)),
            HclBinaryOperator.LessThan => EvaluateComparison(left, right, (a, b) => a < b),
            HclBinaryOperator.GreaterThan => EvaluateComparison(left, right, (a, b) => a > b),
            HclBinaryOperator.LessEqual => EvaluateComparison(left, right, (a, b) => a <= b),
            HclBinaryOperator.GreaterEqual => EvaluateComparison(left, right, (a, b) => a >= b),
            HclBinaryOperator.And => HclValue.FromBool(left.IsTruthy && right.IsTruthy),
            HclBinaryOperator.Or => HclValue.FromBool(left.IsTruthy || right.IsTruthy),
            _ => throw new InvalidOperationException($"Unknown binary operator: {binary.Operator}"),
        };
    }

    /// <summary>Evaluates an arithmetic operation ensuring both operands are numeric.</summary>
    private static HclValue EvaluateArithmetic(
        HclValue left,
        HclValue right,
        Func<double, double, double> operation)
    {
        if (left.Type != HclValueType.Number || right.Type != HclValueType.Number)
        {
            throw new InvalidOperationException(
                $"Cannot perform arithmetic on {left.Type} and {right.Type} values.");
        }

        return HclValue.FromNumber(operation(left.NumberValue, right.NumberValue));
    }

    /// <summary>Evaluates a comparison operation ensuring both operands are numeric.</summary>
    private static HclValue EvaluateComparison(
        HclValue left,
        HclValue right,
        Func<double, double, bool> comparison)
    {
        if (left.Type != HclValueType.Number || right.Type != HclValueType.Number)
        {
            throw new InvalidOperationException(
                $"Cannot compare {left.Type} and {right.Type} values.");
        }

        return HclValue.FromBool(comparison(left.NumberValue, right.NumberValue));
    }

    /// <summary>Evaluates a unary operation (negation or logical not).</summary>
    private HclValue EvaluateUnary(HclUnaryExpression unary, HclEvaluationContext context)
    {
        var operand = EvaluateExpression(unary.Operand, context);

        if (operand.Type == HclValueType.Unknown)
        {
            return operand;
        }

        return unary.Operator switch
        {
            HclUnaryOperator.Negate when operand.Type == HclValueType.Number =>
                HclValue.FromNumber(-operand.NumberValue),
            HclUnaryOperator.Not when operand.Type == HclValueType.Bool =>
                HclValue.FromBool(!operand.BoolValue),
            HclUnaryOperator.Negate =>
                throw new InvalidOperationException($"Cannot negate {operand.Type} value."),
            HclUnaryOperator.Not =>
                throw new InvalidOperationException($"Cannot apply logical not to {operand.Type} value."),
            _ => throw new InvalidOperationException($"Unknown unary operator: {unary.Operator}"),
        };
    }

    /// <summary>Evaluates a conditional expression (ternary) — only the selected branch is evaluated.</summary>
    private HclValue EvaluateConditional(
        HclConditionalExpression conditional,
        HclEvaluationContext context)
    {
        var condition = EvaluateExpression(conditional.Condition, context);

        if (condition.Type == HclValueType.Unknown)
        {
            return condition;
        }

        return condition.IsTruthy
            ? EvaluateExpression(conditional.TrueResult, context)
            : EvaluateExpression(conditional.FalseResult, context);
    }

    /// <summary>
    /// Function calls are not evaluated — they return <see cref="HclValueType.Unknown"/>
    /// with the function name and resolved arguments preserved.
    /// </summary>
    private HclValue EvaluateFunction(
        HclFunctionCallExpression function,
        HclEvaluationContext context)
    {
        var args = new List<HclValue>(function.Arguments.Count);
        foreach (var arg in function.Arguments)
        {
            args.Add(EvaluateExpression(arg, context));
        }

        return HclValue.Unknown(function.Name, args);
    }

    /// <summary>Constructs a tuple value from the resolved elements.</summary>
    private HclValue EvaluateTuple(HclTupleExpression tuple, HclEvaluationContext context)
    {
        var elements = new List<HclValue>(tuple.Elements.Count);
        foreach (var element in tuple.Elements)
        {
            var value = EvaluateExpression(element, context);
            if (value.Type == HclValueType.Unknown)
            {
                return value;
            }

            elements.Add(value);
        }

        return HclValue.FromTuple(elements);
    }

    /// <summary>Constructs an object value from the resolved key-value pairs.</summary>
    private HclValue EvaluateObject(HclObjectExpression obj, HclEvaluationContext context)
    {
        var entries = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        foreach (var element in obj.Elements)
        {
            // Bare identifiers in object keys (e.g. { env = "dev" }) are parsed as
            // HclVariableExpression but should be treated as literal key names.
            // Only when ForceKey is set (i.e. (key) = val syntax) should the key be
            // evaluated as an expression.
            string keyStr;
            if (!element.ForceKey && element.Key is HclVariableExpression varKey)
            {
                keyStr = varKey.Name;
            }
            else
            {
                var key = EvaluateExpression(element.Key, context);
                if (key.Type == HclValueType.Unknown)
                {
                    return key;
                }

                keyStr = key.Type == HclValueType.String ? key.StringValue : key.ToHclString();
            }

            var value = EvaluateExpression(element.Value, context);
            if (value.Type == HclValueType.Unknown)
            {
                return value;
            }

            entries[keyStr] = value;
        }

        return HclValue.FromObject(entries);
    }

    /// <summary>Evaluates a for expression, producing either a tuple or object result.</summary>
    private HclValue EvaluateFor(HclForExpression forExpr, HclEvaluationContext context)
    {
        var collection = EvaluateExpression(forExpr.Collection, context);

        if (collection.Type == HclValueType.Unknown)
        {
            return collection;
        }

        if (forExpr.IsObjectFor)
        {
            return EvaluateForObject(forExpr, collection, context);
        }

        return EvaluateForTuple(forExpr, collection, context);
    }

    /// <summary>Evaluates a for-tuple expression: <c>[for v in list : expr]</c></summary>
    private HclValue EvaluateForTuple(
        HclForExpression forExpr,
        HclValue collection,
        HclEvaluationContext context)
    {
        var result = new List<HclValue>();

        IterateCollection(collection, forExpr, context, (childCtx) =>
        {
            if (forExpr.Condition is not null)
            {
                var condVal = EvaluateExpression(forExpr.Condition, childCtx);
                if (condVal.Type == HclValueType.Unknown || !condVal.IsTruthy)
                {
                    return;
                }
            }

            var value = EvaluateExpression(forExpr.ValueExpression, childCtx);
            result.Add(value);
        });

        return HclValue.FromTuple(result);
    }

    /// <summary>Evaluates a for-object expression: <c>{for k, v in map : k =&gt; expr}</c></summary>
    private HclValue EvaluateForObject(
        HclForExpression forExpr,
        HclValue collection,
        HclEvaluationContext context)
    {
        if (forExpr.IsGrouped)
        {
            return EvaluateForObjectGrouped(forExpr, collection, context);
        }

        var result = new Dictionary<string, HclValue>(StringComparer.Ordinal);

        IterateCollection(collection, forExpr, context, (childCtx) =>
        {
            if (forExpr.Condition is not null)
            {
                var condVal = EvaluateExpression(forExpr.Condition, childCtx);
                if (condVal.Type == HclValueType.Unknown || !condVal.IsTruthy)
                {
                    return;
                }
            }

            var keyVal = EvaluateExpression(forExpr.KeyExpression!, childCtx);
            var valueVal = EvaluateExpression(forExpr.ValueExpression, childCtx);

            result[keyVal.ToHclString()] = valueVal;
        });

        return HclValue.FromObject(result);
    }

    /// <summary>Evaluates a grouped for-object expression: <c>{for k, v in map : k =&gt; v...}</c></summary>
    private HclValue EvaluateForObjectGrouped(
        HclForExpression forExpr,
        HclValue collection,
        HclEvaluationContext context)
    {
        var groups = new Dictionary<string, List<HclValue>>(StringComparer.Ordinal);

        IterateCollection(collection, forExpr, context, (childCtx) =>
        {
            if (forExpr.Condition is not null)
            {
                var condVal = EvaluateExpression(forExpr.Condition, childCtx);
                if (condVal.Type == HclValueType.Unknown || !condVal.IsTruthy)
                {
                    return;
                }
            }

            var keyVal = EvaluateExpression(forExpr.KeyExpression!, childCtx);
            var valueVal = EvaluateExpression(forExpr.ValueExpression, childCtx);

            var key = keyVal.ToHclString();
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(valueVal);
        });

        var result = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        foreach (var kvp in groups)
        {
            result[kvp.Key] = HclValue.FromTuple(kvp.Value);
        }

        return HclValue.FromObject(result);
    }

    /// <summary>
    /// Iterates over a collection (tuple or object) and invokes a callback for each element,
    /// binding the iteration variables in a child scope.
    /// </summary>
    private static void IterateCollection(
        HclValue collection,
        HclForExpression forExpr,
        HclEvaluationContext context,
        Action<HclEvaluationContext> callback)
    {
        switch (collection.Type)
        {
            case HclValueType.Tuple:
                for (int i = 0; i < collection.TupleValue.Count; i++)
                {
                    var childCtx = context.CreateChildScope();
                    // KeyVariable is the iteration variable for tuples (index not bound unless ValueVariable is set)
                    if (forExpr.ValueVariable is not null)
                    {
                        childCtx.SetVariable(forExpr.KeyVariable, HclValue.FromNumber(i));
                        childCtx.SetVariable(forExpr.ValueVariable, collection.TupleValue[i]);
                    }
                    else
                    {
                        childCtx.SetVariable(forExpr.KeyVariable, collection.TupleValue[i]);
                    }

                    callback(childCtx);
                }

                break;

            case HclValueType.Object:
                foreach (var kvp in collection.ObjectValue)
                {
                    var childCtx = context.CreateChildScope();
                    if (forExpr.ValueVariable is not null)
                    {
                        childCtx.SetVariable(forExpr.KeyVariable, HclValue.FromString(kvp.Key));
                        childCtx.SetVariable(forExpr.ValueVariable, kvp.Value);
                    }
                    else
                    {
                        childCtx.SetVariable(forExpr.KeyVariable, kvp.Value);
                    }

                    callback(childCtx);
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot iterate over {collection.Type} value in for expression.");
        }
    }

    /// <summary>Evaluates an index expression: <c>collection[index]</c></summary>
    private HclValue EvaluateIndex(HclIndexExpression index, HclEvaluationContext context)
    {
        var collection = EvaluateExpression(index.Collection, context);
        var indexValue = EvaluateExpression(index.Index, context);

        if (collection.Type == HclValueType.Unknown)
        {
            return collection;
        }

        if (indexValue.Type == HclValueType.Unknown)
        {
            return indexValue;
        }

        return collection.Type switch
        {
            HclValueType.Tuple when indexValue.Type == HclValueType.Number =>
                collection.TupleValue[(int)indexValue.NumberValue],
            HclValueType.Object when indexValue.Type == HclValueType.String =>
                collection.ObjectValue.TryGetValue(indexValue.StringValue, out var val)
                    ? val
                    : throw new HclUnresolvableException(
                        $"Key '{indexValue.StringValue}' not found in object.",
                        index.Start),
            _ => throw new InvalidOperationException(
                $"Cannot index {collection.Type} with {indexValue.Type}."),
        };
    }

    /// <summary>Evaluates an attribute access expression: <c>source.name</c></summary>
    private HclValue EvaluateAttributeAccess(
        HclAttributeAccessExpression access,
        HclEvaluationContext context)
    {
        var source = EvaluateExpression(access.Source, context);

        if (source.Type == HclValueType.Unknown)
        {
            return source;
        }

        if (source.Type != HclValueType.Object)
        {
            throw new InvalidOperationException(
                $"Cannot access attribute '{access.Name}' on {source.Type} value.");
        }

        if (source.ObjectValue.TryGetValue(access.Name, out var value))
        {
            return value;
        }

        throw new HclUnresolvableException(
            access.Name,
            $"Attribute '{access.Name}' not found on object.",
            access.Start);
    }

    /// <summary>Evaluates a splat expression: <c>source[*].attr</c> or <c>source.*.attr</c></summary>
    private HclValue EvaluateSplat(HclSplatExpression splat, HclEvaluationContext context)
    {
        var source = EvaluateExpression(splat.Source, context);

        if (source.Type == HclValueType.Unknown)
        {
            return source;
        }

        if (source.Type != HclValueType.Tuple)
        {
            throw new InvalidOperationException(
                $"Cannot apply splat to {source.Type} value. Expected a tuple.");
        }

        var results = new List<HclValue>(source.TupleValue.Count);
        foreach (var element in source.TupleValue)
        {
            var current = element;
            foreach (var traversal in splat.Traversal)
            {
                if (current.Type == HclValueType.Unknown)
                {
                    break;
                }

                current = traversal switch
                {
                    HclAttributeAccessExpression attrAccess when current.Type == HclValueType.Object =>
                        current.ObjectValue.TryGetValue(attrAccess.Name, out var val)
                            ? val
                            : throw new HclUnresolvableException(
                                attrAccess.Name,
                                $"Attribute '{attrAccess.Name}' not found in splat traversal.",
                                attrAccess.Start),
                    HclIndexExpression idxExpr =>
                        EvaluateIndex(
                            new HclIndexExpression
                            {
                                Collection = WrapAsLiteralSource(current),
                                Index = idxExpr.Index,
                            },
                            context),
                    _ => throw new InvalidOperationException(
                        $"Unsupported traversal type in splat: {traversal.GetType().Name}"),
                };
            }

            results.Add(current);
        }

        return HclValue.FromTuple(results);
    }

    /// <summary>Evaluates a template expression by resolving interpolations and concatenating parts.</summary>
    private HclValue EvaluateTemplate(HclTemplateExpression template, HclEvaluationContext context)
    {
        // If Parts is empty, the template has no interpolations — return raw content as string
        if (template.Parts.Count == 0)
        {
            return HclValue.FromString(template.RawContent);
        }

        // Resolve all parts (alternating literal strings and expressions)
        var sb = new System.Text.StringBuilder();
        foreach (var part in template.Parts)
        {
            var value = EvaluateExpression(part, context);
            if (value.Type == HclValueType.Unknown)
            {
                return value;
            }

            sb.Append(value.ToHclString());
        }

        return HclValue.FromString(sb.ToString());
    }

    /// <summary>
    /// Evaluates a template wrap expression. In HCL, <c>"${expr}"</c> unwraps to the raw value
    /// of the inner expression (not necessarily a string).
    /// </summary>
    private HclValue EvaluateTemplateWrap(
        HclTemplateWrapExpression wrap,
        HclEvaluationContext context)
    {
        return EvaluateExpression(wrap.Wrapped, context);
    }

    /// <summary>
    /// Wraps an already-resolved value as a synthetic literal expression node,
    /// used internally for splat index traversal.
    /// </summary>
    private static HclLiteralExpression WrapAsLiteralSource(HclValue value) => value.Type switch
    {
        HclValueType.String => new HclLiteralExpression
        {
            Value = value.StringValue,
            Kind = HclLiteralKind.String,
        },
        HclValueType.Number => new HclLiteralExpression
        {
            Value = value.NumberValue.ToString(CultureInfo.InvariantCulture),
            Kind = HclLiteralKind.Number,
        },
        _ => throw new InvalidOperationException(
            $"Cannot wrap {value.Type} as a literal expression for splat traversal."),
    };
}
