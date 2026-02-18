using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Types;

/// <summary>
/// Parses HCL expressions (from a <c>type = ...</c> attribute) into <see cref="TerraformType"/> instances.
/// Handles bare identifiers, function-call syntax for collections and <c>optional()</c>,
/// and object/tuple structural syntax.
/// </summary>
internal static class TerraformTypeParser
{
    /// <summary>
    /// Parses an HCL expression into a <see cref="TerraformType"/>.
    /// </summary>
    /// <param name="expression">The expression from a variable's <c>type</c> attribute.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="expression"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException">When the expression is not a valid type constraint.</exception>
    public static TerraformType Parse(HclExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return ParseCore(expression);
    }

    private static TerraformType ParseCore(HclExpression expression) => expression switch
    {
        // Bare identifier: string, number, bool, any
        HclVariableExpression variable => ParsePrimitive(variable.Name),

        // Function-call syntax: list(T), set(T), map(T), object({...}), tuple([...]), optional(T, default)
        HclFunctionCallExpression func => ParseFunctionCall(func),

        _ => throw new FormatException(
            $"Cannot parse type constraint from expression type '{expression.GetType().Name}'."),
    };

    private static TerraformType ParsePrimitive(string name) => name switch
    {
        "string" => TerraformType.String,
        "number" => TerraformType.Number,
        "bool" => TerraformType.Bool,
        "any" => TerraformType.Any,
        _ => throw new FormatException($"Unknown primitive type: '{name}'."),
    };

    private static TerraformType ParseFunctionCall(HclFunctionCallExpression func) => func.Name switch
    {
        "list" => ParseCollectionType(TerraformTypeKind.List, func),
        "set" => ParseCollectionType(TerraformTypeKind.Set, func),
        "map" => ParseCollectionType(TerraformTypeKind.Map, func),
        "object" => ParseObjectType(func),
        "tuple" => ParseTupleType(func),
        "optional" => throw new FormatException(
            "The 'optional()' modifier can only appear inside object type fields."),
        _ => throw new FormatException($"Unknown type function: '{func.Name}'."),
    };

    private static TerraformCollectionType ParseCollectionType(TerraformTypeKind kind, HclFunctionCallExpression func)
    {
        if (func.Arguments.Count != 1)
        {
            throw new FormatException(
                $"'{func.Name}()' expects exactly 1 argument, got {func.Arguments.Count}.");
        }

        var elementType = ParseCore(func.Arguments[0]);

        return new TerraformCollectionType(kind, elementType);
    }

    private static TerraformObjectType ParseObjectType(HclFunctionCallExpression func)
    {
        if (func.Arguments.Count != 1)
        {
            throw new FormatException(
                $"'object()' expects exactly 1 argument, got {func.Arguments.Count}.");
        }

        if (func.Arguments[0] is not HclObjectExpression obj)
        {
            throw new FormatException(
                "The argument to 'object()' must be an object expression ({ ... }).");
        }

        var fields = new List<TerraformObjectField>(obj.Elements.Count);

        foreach (var element in obj.Elements)
        {
            var fieldName = ExtractFieldName(element);
            var (fieldType, isOptional, defaultValue) = ParseFieldType(element.Value);
            fields.Add(new TerraformObjectField(fieldName, fieldType, isOptional, defaultValue));
        }

        return new TerraformObjectType(fields);
    }

    private static TerraformTupleType ParseTupleType(HclFunctionCallExpression func)
    {
        if (func.Arguments.Count != 1)
        {
            throw new FormatException(
                $"'tuple()' expects exactly 1 argument, got {func.Arguments.Count}.");
        }

        if (func.Arguments[0] is not HclTupleExpression tuple)
        {
            throw new FormatException(
                "The argument to 'tuple()' must be a tuple expression ([ ... ]).");
        }

        var elements = new List<TerraformType>(tuple.Elements.Count);

        foreach (var element in tuple.Elements)
        {
            elements.Add(ParseCore(element));
        }

        return new TerraformTupleType(elements);
    }

    /// <summary>
    /// Parses a field type, detecting <c>optional(type)</c> or <c>optional(type, default)</c> wrappers.
    /// </summary>
    private static (TerraformType Type, bool IsOptional, HclExpression? Default) ParseFieldType(
        HclExpression expression)
    {
        if (expression is HclFunctionCallExpression { Name: "optional" } optionalFunc)
        {
            if (optionalFunc.Arguments.Count is < 1 or > 2)
            {
                throw new FormatException(
                    $"'optional()' expects 1 or 2 arguments, got {optionalFunc.Arguments.Count}.");
            }

            var innerType = ParseCore(optionalFunc.Arguments[0]);
            var defaultValue = optionalFunc.Arguments.Count == 2
                ? optionalFunc.Arguments[1]
                : null;

            return (innerType, true, defaultValue);
        }

        return (ParseCore(expression), false, null);
    }

    private static string ExtractFieldName(HclObjectElement element)
    {
        // Object element keys in type constraints are bare identifiers (HclVariableExpression)
        // or string literals (HclLiteralExpression with Kind == String)
        return element.Key switch
        {
            HclVariableExpression v => v.Name,
            HclLiteralExpression { Kind: HclLiteralKind.String } lit => lit.Value
                ?? throw new FormatException("Object field name cannot be null."),
            _ => throw new FormatException(
                $"Object field key must be an identifier or string, got '{element.Key.GetType().Name}'."),
        };
    }
}
