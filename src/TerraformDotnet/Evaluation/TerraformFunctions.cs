using System.Collections.Frozen;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

/// <summary>
/// A restricted, deterministic implementation of the Terraform built-in function library for
/// evaluating <c>validation</c> conditions. It plugs into <see cref="HclEvaluator"/> through
/// <see cref="HclEvaluatorOptions.FunctionResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// Supported are the numeric, string, collection, type-conversion and encoding functions plus the
/// <c>cidr*</c> family, <c>format</c>/<c>formatlist</c> and <c>regex</c>/<c>regexall</c>. Functions
/// that depend on the environment or are non-deterministic (<c>file</c>, <c>timestamp</c>, <c>uuid</c>,
/// provider functions, ...) and unsupported formatting or regular expression features are reported
/// as "not supported": <see cref="Invoke"/> returns <c>null</c> and the evaluator keeps the result
/// unknown, so a validation is never wrongly reported as failed.
/// </para>
/// <para>
/// Invalid arguments throw <see cref="HclFunctionException"/>, matching Terraform's function
/// errors, which <c>can(...)</c> and <c>try(...)</c> recover from. Numbers are IEEE doubles, so
/// results beyond 15 significant digits can differ from Terraform's arbitrary-precision numbers.
/// </para>
/// </remarks>
public sealed partial class TerraformFunctions : IHclFunctionResolver
{
    private delegate HclValue? Implementation(FunctionCall call);

    private readonly record struct Entry(int MinArguments, int MaxArguments, Implementation Run);

    private static readonly FrozenDictionary<string, Entry> Table = BuildTable();

    /// <summary>Gets a shared, stateless instance of the function library.</summary>
    public static TerraformFunctions Default { get; } = new();

    /// <summary>Gets the names of all functions this library evaluates.</summary>
    public static IReadOnlyCollection<string> SupportedFunctions { get; } = Table.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Determines whether the named function is evaluated by this library.</summary>
    /// <param name="name">The function name.</param>
    /// <returns><c>true</c> when the function is supported.</returns>
    public static bool IsSupported(string name) => Table.ContainsKey(name);

    /// <inheritdoc />
    public HclValue? Invoke(string name, IReadOnlyList<HclValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Table.TryGetValue(name, out var entry))
        {
            return null;
        }

        var call = new FunctionCall(name, arguments);
        if (arguments.Count < entry.MinArguments || (entry.MaxArguments >= 0 && arguments.Count > entry.MaxArguments))
        {
            var expected = entry.MaxArguments < 0
                ? $"at least {entry.MinArguments}"
                : entry.MinArguments == entry.MaxArguments
                    ? entry.MinArguments.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : $"{entry.MinArguments} to {entry.MaxArguments}";

            throw call.Error($"expected {expected} argument(s), got {arguments.Count}.");
        }

        return entry.Run(call);
    }

    private static FrozenDictionary<string, Entry> BuildTable()
    {
        var table = new Dictionary<string, Entry>(StringComparer.Ordinal);
        AddNumeric(table);
        AddStrings(table);
        AddCollections(table);
        AddConversions(table);
        AddEncoding(table);
        AddCidr(table);
        AddFormat(table);
        AddRegex(table);

        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void Add(Dictionary<string, Entry> table, string name, int min, int max, Implementation run)
        => table[name] = new Entry(min, max, run);

    private static HclValue Str(string value) => HclValue.FromString(value);

    private static HclValue Num(double value) => HclValue.FromNumber(value);

    private static HclValue Bool(bool value) => HclValue.FromBool(value);

    private static HclValue List(IList<HclValue> values) => HclValue.FromTuple(values);
}
