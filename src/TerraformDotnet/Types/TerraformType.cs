using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Types;

/// <summary>
/// Abstract base class for all Terraform type constraints.
/// Provides static factory properties for primitive types and factory methods
/// for collection and structural types.
/// <example>
/// <code>
/// var stringType = TerraformType.String;
/// var listType = TerraformType.List(TerraformType.String);
/// var objType = TerraformType.Object([new TerraformObjectField("name", TerraformType.String)]);
/// </code>
/// </example>
/// </summary>
public abstract class TerraformType : IEquatable<TerraformType>
{
    /// <summary>Gets the kind of this type.</summary>
    public abstract TerraformTypeKind Kind { get; }

    /// <summary>Emits the type back to HCL syntax (e.g. "string", "list(string)", "object({ name = string })").</summary>
    public abstract string ToHcl();

    /// <summary>Gets the singleton <c>string</c> type.</summary>
    public static TerraformType String { get; } = new TerraformPrimitiveType(TerraformTypeKind.String);

    /// <summary>Gets the singleton <c>number</c> type.</summary>
    public static TerraformType Number { get; } = new TerraformPrimitiveType(TerraformTypeKind.Number);

    /// <summary>Gets the singleton <c>bool</c> type.</summary>
    public static TerraformType Bool { get; } = new TerraformPrimitiveType(TerraformTypeKind.Bool);

    /// <summary>Gets the singleton <c>any</c> type.</summary>
    public static TerraformType Any { get; } = new TerraformPrimitiveType(TerraformTypeKind.Any);

    /// <summary>Creates a <c>list(element)</c> type.</summary>
    /// <param name="element">The element type.</param>
    public static TerraformCollectionType List(TerraformType element) =>
        new(TerraformTypeKind.List, element);

    /// <summary>Creates a <c>set(element)</c> type.</summary>
    /// <param name="element">The element type.</param>
    public static TerraformCollectionType Set(TerraformType element) =>
        new(TerraformTypeKind.Set, element);

    /// <summary>Creates a <c>map(element)</c> type.</summary>
    /// <param name="element">The element type.</param>
    public static TerraformCollectionType Map(TerraformType element) =>
        new(TerraformTypeKind.Map, element);

    /// <summary>Creates an <c>object({ ... })</c> type.</summary>
    /// <param name="fields">The object fields.</param>
    public static TerraformObjectType Object(IReadOnlyList<TerraformObjectField> fields) =>
        new(fields);

    /// <summary>Creates a <c>tuple([...])</c> type.</summary>
    /// <param name="elements">The positional element types.</param>
    public static TerraformTupleType Tuple(IReadOnlyList<TerraformType> elements) =>
        new(elements);

    /// <summary>
    /// Parses a Terraform type constraint from an HCL expression.
    /// Handles bare identifiers (<c>string</c>), function-call syntax (<c>list(string)</c>),
    /// and nested structural types (<c>object({ name = optional(string, "default") })</c>).
    /// </summary>
    /// <param name="expression">The HCL expression representing the type constraint.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="expression"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException">When the expression cannot be parsed as a valid type.</exception>
    public static TerraformType Parse(HclExpression expression) =>
        TerraformTypeParser.Parse(expression);

    /// <inheritdoc />
    public abstract bool Equals(TerraformType? other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerraformType other && Equals(other);

    /// <inheritdoc />
    public abstract override int GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToHcl();
}

/// <summary>
/// A primitive Terraform type: <c>string</c>, <c>number</c>, <c>bool</c>, or <c>any</c>.
/// </summary>
internal sealed class TerraformPrimitiveType(TerraformTypeKind kind) : TerraformType
{
    /// <inheritdoc />
    public override TerraformTypeKind Kind => kind;

    /// <inheritdoc />
    public override string ToHcl() => kind switch
    {
        TerraformTypeKind.String => "string",
        TerraformTypeKind.Number => "number",
        TerraformTypeKind.Bool => "bool",
        TerraformTypeKind.Any => "any",
        _ => throw new InvalidOperationException($"Unexpected primitive kind: {kind}"),
    };

    /// <inheritdoc />
    public override bool Equals(TerraformType? other) =>
        other is TerraformPrimitiveType p && p.Kind == Kind;

    /// <inheritdoc />
    public override int GetHashCode() => Kind.GetHashCode();
}
