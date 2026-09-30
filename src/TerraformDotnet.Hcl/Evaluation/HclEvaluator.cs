using System.Globalization;
using TerraformDotnet.Hcl.Exceptions;
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
/// Function calls are delegated to the <see cref="IHclFunctionResolver"/> configured through
/// <see cref="HclEvaluatorOptions"/>. Without a resolver — or when the resolver does not support
/// a function — they return <see cref="HclValue.Unknown(string, IList{HclValue}?)"/> with the
/// function name and resolved arguments preserved. <c>can</c> and <c>try</c> are always evaluated
/// by the evaluator itself because they need lazy argument evaluation.
/// </para>
/// <para>
/// Unknown values propagate using three-valued (Kleene) logic: <c>false &amp;&amp; unknown</c> is
/// <c>false</c>, <c>true || unknown</c> is <c>true</c>, everything else involving an unknown is
/// unknown. <c>&amp;&amp;</c> and <c>||</c> short-circuit. Operands are converted like Terraform does
/// (<c>"5" &lt; 10</c> compares numbers, <c>"true" &amp;&amp; x</c> is a boolean operation) except for
/// equality, which never converts.
/// </para>
/// <para>An instance is not thread-safe; use one evaluator per thread.</para>
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
    private readonly HclEvaluatorOptions _options;
    private int _depth;
    private int _iterations;

    /// <summary>Initializes a new evaluator with default options (no function resolver).</summary>
    public HclEvaluator()
        : this(new HclEvaluatorOptions())
    {
    }

    /// <summary>Initializes a new evaluator with the given options.</summary>
    /// <param name="options">The evaluator options.</param>
    public HclEvaluator(HclEvaluatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <summary>
    /// Evaluates an HCL expression to a resolved value using the given evaluation context.
    /// </summary>
    /// <param name="expression">The expression AST node to evaluate.</param>
    /// <param name="context">The variable bindings available during evaluation.</param>
    /// <returns>The resolved <see cref="HclValue"/>.</returns>
    /// <exception cref="HclUnresolvableException">Thrown when a referenced variable is not found.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an expression type is not supported or an operation is applied to unsuitable operands.</exception>
    /// <exception cref="HclEvaluationLimitException">Thrown when evaluation exceeds the configured depth or iteration limit.</exception>
    public HclValue Evaluate(HclExpression expression, HclEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(context);

        _depth = 0;
        _iterations = 0;

        return EvaluateExpression(expression, context);
    }

    /// <summary>Recursively evaluates any expression node.</summary>
    private HclValue EvaluateExpression(HclExpression expression, HclEvaluationContext context)
    {
        _depth++;
        if (_depth > _options.MaxDepth)
        {
            throw new HclEvaluationLimitException(
                $"Expression evaluation exceeded maximum recursion depth of {_options.MaxDepth}.");
        }

        try
        {
            return expression switch
            {
                HclLiteralExpression literal => EvaluateLiteral(literal, context),
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
    private HclValue EvaluateLiteral(HclLiteralExpression literal, HclEvaluationContext context) => literal.Kind switch
    {
        HclLiteralKind.Null => HclValue.Null,
        HclLiteralKind.Bool => HclValue.FromBool(
            literal.Value is not null &&
            literal.Value.Equals("true", StringComparison.OrdinalIgnoreCase)),
        HclLiteralKind.Number => HclValue.FromNumber(
            double.Parse(literal.Value!, CultureInfo.InvariantCulture)),
        HclLiteralKind.String => literal.Value is { } text && HclStringTemplate.MayBeTemplate(text)
            ? EvaluateStringTemplate(literal, context)
            : HclValue.FromString(literal.Value ?? string.Empty),
        _ => throw new InvalidOperationException($"Unknown literal kind: {literal.Kind}"),
    };

    /// <summary>
    /// Evaluates a string containing <c>${...}</c> interpolations. A string that consists of a single
    /// interpolation yields the interpolated value unchanged (not converted to a string), like Terraform.
    /// </summary>
    private HclValue EvaluateStringTemplate(HclLiteralExpression literal, HclEvaluationContext context)
    {
        var template = HclStringTemplate.For(literal);
        if (template is null)
        {
            return HclValue.Unknown("template");
        }

        if (template.Parts.Count == 1 && template.Parts[0] is HclExpression single)
        {
            return EvaluateExpression(single, context);
        }

        var builder = new System.Text.StringBuilder();
        foreach (var part in template.Parts)
        {
            if (part is string text)
            {
                builder.Append(text);
                continue;
            }

            var value = EvaluateExpression((HclExpression)part, context);
            switch (value.Type)
            {
                case HclValueType.Unknown:
                    return value;
                case HclValueType.Null or HclValueType.Tuple or HclValueType.Object:
                    throw new InvalidOperationException(
                        $"Cannot include a {value.Type} value in a string template.");
                default:
                    builder.Append(value.ToHclString());
                    break;
            }
        }

        return HclValue.FromString(builder.ToString());
    }

    /// <summary>Looks up a variable reference in the evaluation context.</summary>
    private HclValue EvaluateVariable(
        HclVariableExpression variable,
        HclEvaluationContext context)
    {
        if (context.TryGetVariable(variable.Name, out var value))
        {
            return value;
        }

        if (_options.TreatUndefinedVariablesAsUnknown)
        {
            return HclValue.Unknown(variable.Name);
        }

        throw new HclUnresolvableException(
            variable.Name,
            $"Variable '{variable.Name}' is not defined in the evaluation context.",
            variable.Start);
    }

    /// <summary>Evaluates a binary operation on two resolved operands.</summary>
    private HclValue EvaluateBinary(HclBinaryExpression binary, HclEvaluationContext context)
    {
        if (binary.Operator is HclBinaryOperator.And or HclBinaryOperator.Or)
        {
            return EvaluateLogical(binary, context);
        }

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
            HclBinaryOperator.Modulo => EvaluateModulo(left, right),
            HclBinaryOperator.Equal => HclValue.FromBool(left.Equals(right)),
            HclBinaryOperator.NotEqual => HclValue.FromBool(!left.Equals(right)),
            HclBinaryOperator.LessThan => EvaluateComparison(left, right, (a, b) => a < b),
            HclBinaryOperator.GreaterThan => EvaluateComparison(left, right, (a, b) => a > b),
            HclBinaryOperator.LessEqual => EvaluateComparison(left, right, (a, b) => a <= b),
            HclBinaryOperator.GreaterEqual => EvaluateComparison(left, right, (a, b) => a >= b),
            _ => throw new InvalidOperationException($"Unknown binary operator: {binary.Operator}"),
        };
    }

    /// <summary>
    /// Evaluates <c>&amp;&amp;</c> and <c>||</c> with short-circuiting and three-valued logic.
    /// </summary>
    /// <remarks>
    /// A definite short-circuit value (<c>false</c> for and, <c>true</c> for or) on either side decides
    /// the result even when the other side is unknown; this is what keeps guards such as
    /// <c>var.x != null &amp;&amp; length(var.x) &gt; 0</c> evaluable.
    /// </remarks>
    private HclValue EvaluateLogical(HclBinaryExpression binary, HclEvaluationContext context)
    {
        var isAnd = binary.Operator == HclBinaryOperator.And;
        var decisive = !isAnd;

        var left = ToBoolOperand(EvaluateExpression(binary.Left, context), binary.Operator);
        if (left.Type == HclValueType.Bool && left.BoolValue == decisive)
        {
            return left;
        }

        var right = ToBoolOperand(EvaluateExpression(binary.Right, context), binary.Operator);
        if (right.Type == HclValueType.Bool && right.BoolValue == decisive)
        {
            return right;
        }

        if (left.Type == HclValueType.Unknown)
        {
            return left;
        }

        if (right.Type == HclValueType.Unknown)
        {
            return right;
        }

        return HclValue.FromBool(!decisive);
    }

    /// <summary>Converts a logical operand to a boolean, passing unknown values through.</summary>
    private static HclValue ToBoolOperand(HclValue value, object operation)
    {
        return value.Type switch
        {
            HclValueType.Bool or HclValueType.Unknown => value,
            HclValueType.String when value.StringValue == "true" => HclValue.True,
            HclValueType.String when value.StringValue == "false" => HclValue.False,
            _ => throw new InvalidOperationException(
                $"Cannot apply {operation} to {value.Type} value; a boolean is required."),
        };
    }

    /// <summary>Converts an arithmetic or comparison operand to a number, parsing numeric strings.</summary>
    private static double ToNumberOperand(HclValue value, string operation)
    {
        if (value.Type == HclValueType.Number)
        {
            return value.NumberValue;
        }

        if (value.Type == HclValueType.String
            && double.TryParse(
                value.StringValue,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var parsed)
            && double.IsFinite(parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Cannot {operation}: {value.Type} value is not a number.");
    }

    /// <summary>Evaluates an arithmetic operation, converting numeric strings.</summary>
    private static HclValue EvaluateArithmetic(
        HclValue left,
        HclValue right,
        Func<double, double, double> operation)
    {
        return HclValue.FromNumber(operation(
            ToNumberOperand(left, "perform arithmetic"),
            ToNumberOperand(right, "perform arithmetic")));
    }

    /// <summary>Evaluates a modulo operation; a zero divisor is an evaluation error.</summary>
    private static HclValue EvaluateModulo(HclValue left, HclValue right)
    {
        var divisor = ToNumberOperand(right, "perform arithmetic");
        if (divisor == 0)
        {
            throw new InvalidOperationException("Cannot perform modulo: division by zero.");
        }

        return HclValue.FromNumber(ToNumberOperand(left, "perform arithmetic") % divisor);
    }

    /// <summary>Evaluates a comparison operation, converting numeric strings.</summary>
    private static HclValue EvaluateComparison(
        HclValue left,
        HclValue right,
        Func<double, double, bool> comparison)
    {
        return HclValue.FromBool(comparison(
            ToNumberOperand(left, "compare"),
            ToNumberOperand(right, "compare")));
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
            HclUnaryOperator.Negate => HclValue.FromNumber(-ToNumberOperand(operand, "negate")),
            HclUnaryOperator.Not => HclValue.FromBool(!ToBoolOperand(operand, unary.Operator).BoolValue),
            _ => throw new InvalidOperationException($"Unknown unary operator: {unary.Operator}"),
        };
    }

    /// <summary>Evaluates a conditional expression (ternary) — only the selected branch is evaluated.</summary>
    private HclValue EvaluateConditional(
        HclConditionalExpression conditional,
        HclEvaluationContext context)
    {
        var condition = ToBoolOperand(EvaluateExpression(conditional.Condition, context), "the conditional operator");

        if (condition.Type == HclValueType.Unknown)
        {
            return condition;
        }

        return condition.BoolValue
            ? EvaluateExpression(conditional.TrueResult, context)
            : EvaluateExpression(conditional.FalseResult, context);
    }

    /// <summary>
    /// Evaluates a function call. <c>can</c> and <c>try</c> are handled here (they need lazy arguments);
    /// everything else goes to the configured <see cref="IHclFunctionResolver"/>, and stays
    /// <see cref="HclValueType.Unknown"/> when there is none, when an argument is unknown, or when the
    /// resolver does not support the function.
    /// </summary>
    private HclValue EvaluateFunction(
        HclFunctionCallExpression function,
        HclEvaluationContext context)
    {
        if (!function.ExpandFinalArgument)
        {
            if (function.Name == "can" && function.Arguments.Count == 1)
            {
                return EvaluateCan(function.Arguments[0], context);
            }

            if (function.Name == "try" && function.Arguments.Count >= 1)
            {
                return EvaluateTry(function.Arguments, context);
            }
        }

        var args = new List<HclValue>(function.Arguments.Count);
        foreach (var arg in function.Arguments)
        {
            args.Add(EvaluateExpression(arg, context));
        }

        if (function.ExpandFinalArgument && args.Count > 0)
        {
            var last = args[^1];
            if (last.Type == HclValueType.Tuple)
            {
                args.RemoveAt(args.Count - 1);
                args.AddRange(last.TupleValue);
            }
            else if (last.Type != HclValueType.Unknown)
            {
                throw new InvalidOperationException(
                    $"Cannot expand a {last.Type} value into the arguments of '{function.Name}'; a tuple is required.");
            }
        }

        if (_options.FunctionResolver is null || args.Any(a => a.Type == HclValueType.Unknown))
        {
            return HclValue.Unknown(function.Name, args);
        }

        return _options.FunctionResolver.Invoke(function.Name, args)
            ?? HclValue.Unknown(function.Name, args);
    }

    /// <summary><c>can(expr)</c>: <c>true</c> when the expression evaluates without error.</summary>
    private HclValue EvaluateCan(HclExpression argument, HclEvaluationContext context)
    {
        try
        {
            var value = EvaluateExpression(argument, context);

            return value.Type == HclValueType.Unknown ? value : HclValue.True;
        }
        catch (Exception ex) when (IsRecoverableEvaluationError(ex))
        {
            return HclValue.False;
        }
    }

    /// <summary><c>try(a, b, …)</c>: the first argument that evaluates without error.</summary>
    private HclValue EvaluateTry(List<HclExpression> arguments, HclEvaluationContext context)
    {
        Exception? lastError = null;
        foreach (var argument in arguments)
        {
            try
            {
                return EvaluateExpression(argument, context);
            }
            catch (Exception ex) when (IsRecoverableEvaluationError(ex))
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"All arguments of try() failed. Last error: {lastError?.Message}", lastError);
    }

    /// <summary>
    /// Determines whether an exception is an ordinary evaluation error that <c>can</c>/<c>try</c> may absorb.
    /// Limit violations are excluded so an aborted evaluation is never disguised as a normal result.
    /// </summary>
    private static bool IsRecoverableEvaluationError(Exception ex) => ex switch
    {
        HclEvaluationLimitException => false,
        InvalidOperationException or HclException or ArgumentException or FormatException
            or OverflowException or KeyNotFoundException or InvalidCastException => true,
        _ => false,
    };

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

    /// <summary>
    /// Evaluates the optional <c>if</c> clause of a for expression.
    /// </summary>
    /// <returns>
    /// <c>true</c> to keep the element, <c>false</c> to skip it, or the unknown value when the
    /// condition cannot be decided (which makes the whole for expression unknown).
    /// </returns>
    private bool? EvaluateForCondition(
        HclForExpression forExpr,
        HclEvaluationContext childCtx,
        out HclValue unknown)
    {
        unknown = HclValue.Null;
        if (forExpr.Condition is null)
        {
            return true;
        }

        var condition = ToBoolOperand(EvaluateExpression(forExpr.Condition, childCtx), "the for expression condition");
        if (condition.Type == HclValueType.Unknown)
        {
            unknown = condition;

            return null;
        }

        return condition.BoolValue;
    }

    /// <summary>Evaluates a for-tuple expression: <c>[for v in list : expr]</c></summary>
    private HclValue EvaluateForTuple(
        HclForExpression forExpr,
        HclValue collection,
        HclEvaluationContext context)
    {
        var result = new List<HclValue>();

        foreach (var childCtx in IterateCollection(collection, forExpr, context))
        {
            var keep = EvaluateForCondition(forExpr, childCtx, out var unknownCondition);
            if (keep is null)
            {
                return unknownCondition;
            }

            if (keep == false)
            {
                continue;
            }

            var value = EvaluateExpression(forExpr.ValueExpression, childCtx);
            if (value.Type == HclValueType.Unknown)
            {
                return value;
            }

            result.Add(value);
        }

        return HclValue.FromTuple(result);
    }

    /// <summary>Evaluates a for-object expression: <c>{for k, v in map : k =&gt; expr}</c></summary>
    private HclValue EvaluateForObject(
        HclForExpression forExpr,
        HclValue collection,
        HclEvaluationContext context)
    {
        var grouped = forExpr.IsGrouped;
        var result = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<HclValue>>(StringComparer.Ordinal);

        foreach (var childCtx in IterateCollection(collection, forExpr, context))
        {
            var keep = EvaluateForCondition(forExpr, childCtx, out var unknownCondition);
            if (keep is null)
            {
                return unknownCondition;
            }

            if (keep == false)
            {
                continue;
            }

            var keyVal = EvaluateExpression(forExpr.KeyExpression!, childCtx);
            if (keyVal.Type == HclValueType.Unknown)
            {
                return keyVal;
            }

            var valueVal = EvaluateExpression(forExpr.ValueExpression, childCtx);
            if (valueVal.Type == HclValueType.Unknown)
            {
                return valueVal;
            }

            var key = keyVal.ToHclString();
            if (!grouped)
            {
                result[key] = valueVal;

                continue;
            }

            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(valueVal);
        }

        if (!grouped)
        {
            return HclValue.FromObject(result);
        }

        foreach (var kvp in groups)
        {
            result[kvp.Key] = HclValue.FromTuple(kvp.Value);
        }

        return HclValue.FromObject(result);
    }

    /// <summary>
    /// Iterates over a collection (tuple or object), yielding a child scope with the iteration
    /// variables bound for each element. Enforces <see cref="HclEvaluatorOptions.MaxIterations"/>.
    /// </summary>
    private IEnumerable<HclEvaluationContext> IterateCollection(
        HclValue collection,
        HclForExpression forExpr,
        HclEvaluationContext context)
    {
        switch (collection.Type)
        {
            case HclValueType.Tuple:
                for (int i = 0; i < collection.TupleValue.Count; i++)
                {
                    CountIteration();
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

                    yield return childCtx;
                }

                break;

            case HclValueType.Object:
                // Terraform iterates object and map attributes in lexical key order.
                foreach (var kvp in collection.ObjectValue.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    CountIteration();
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

                    yield return childCtx;
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot iterate over {collection.Type} value in for expression.");
        }
    }

    /// <summary>Counts one loop iteration and aborts when the configured limit is exceeded.</summary>
    private void CountIteration()
    {
        if (++_iterations > _options.MaxIterations)
        {
            throw new HclEvaluationLimitException(
                $"Expression evaluation exceeded the maximum of {_options.MaxIterations} loop iterations.");
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

        return IndexValue(collection, indexValue, index);
    }

    /// <summary>
    /// Indexes an already evaluated collection. Tuple indexes may be numeric strings
    /// (Terraform converts them); out-of-range and missing keys are evaluation errors.
    /// </summary>
    private static HclValue IndexValue(HclValue collection, HclValue indexValue, HclExpression at)
    {
        switch (collection.Type)
        {
            case HclValueType.Tuple:
                var position = ToNumberOperand(indexValue, "index a tuple");
                if (position != Math.Floor(position) || position < 0 || position >= collection.TupleValue.Count)
                {
                    throw new HclUnresolvableException(
                        $"Index {indexValue.ToHclString()} is out of range for a tuple of {collection.TupleValue.Count} element(s).",
                        at.Start);
                }

                return collection.TupleValue[(int)position];

            case HclValueType.Object when indexValue.Type == HclValueType.String:
                return collection.ObjectValue.TryGetValue(indexValue.StringValue, out var val)
                    ? val
                    : throw new HclUnresolvableException(
                        $"Key '{indexValue.StringValue}' not found in object.",
                        at.Start);

            default:
                throw new InvalidOperationException(
                    $"Cannot index {collection.Type} with {indexValue.Type}.");
        }
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

        // Legacy tuple index syntax: list.0
        if (source.Type == HclValueType.Tuple && int.TryParse(access.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var legacyIndex))
        {
            return IndexValue(source, HclValue.FromNumber(legacyIndex), access);
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

    /// <summary>
    /// Evaluates a splat expression: <c>source[*].attr</c> or <c>source.*.attr</c>.
    /// Like Terraform, a null source yields an empty tuple and a single non-tuple value is treated
    /// as a one-element tuple.
    /// </summary>
    private HclValue EvaluateSplat(HclSplatExpression splat, HclEvaluationContext context)
    {
        var source = EvaluateExpression(splat.Source, context);

        if (source.Type == HclValueType.Unknown)
        {
            return source;
        }

        IReadOnlyList<HclValue> elements = source.Type switch
        {
            HclValueType.Null => [],
            HclValueType.Tuple => source.TupleValue,
            _ => [source],
        };

        var results = new List<HclValue>(elements.Count);
        foreach (var element in elements)
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
                    HclIndexExpression idxExpr => EvaluateSplatIndex(current, idxExpr, context),
                    _ => throw new InvalidOperationException(
                        $"Unsupported traversal type in splat: {traversal.GetType().Name}"),
                };
            }

            if (current.Type == HclValueType.Unknown)
            {
                return current;
            }

            results.Add(current);
        }

        return HclValue.FromTuple(results);
    }

    /// <summary>Applies an index traversal step of a splat expression to one element.</summary>
    private HclValue EvaluateSplatIndex(HclValue element, HclIndexExpression idxExpr, HclEvaluationContext context)
    {
        var indexValue = EvaluateExpression(idxExpr.Index, context);

        return indexValue.Type == HclValueType.Unknown
            ? indexValue
            : IndexValue(element, indexValue, idxExpr);
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
}
