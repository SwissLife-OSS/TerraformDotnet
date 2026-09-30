using System.Text.RegularExpressions;
using TerraformDotnet.Emit;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Validation;

/// <summary>
/// The <c>locals</c> of a module, evaluated lazily for <c>validation</c> conditions. Only the
/// locals a condition refers to (transitively) are evaluated; a local that cannot be evaluated
/// (unsupported function, reference to a resource, circular reference) becomes an unknown value.
/// </summary>
internal sealed partial class ModuleLocals
{
    private readonly Dictionary<string, HclExpression> _expressions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _dependencies = new(StringComparer.Ordinal);

    public ModuleLocals(TerraformModule module)
    {
        foreach (var local in module.Locals)
        {
            _expressions.TryAdd(local.Name, local.Value);
        }

        foreach (var (name, expression) in _expressions)
        {
            _dependencies[name] = FindReferences(TextOf(expression)).ToArray();
        }
    }

    /// <summary>Gets the source text of an expression; an unemittable expression yields an empty string.</summary>
    public static string TextOf(HclExpression expression)
    {
        try
        {
            return ModuleCallEmitter.EmitExpression(expression);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>Finds the names of the module's locals that the given source text refers to (<c>local.name</c>).</summary>
    public IEnumerable<string> FindReferences(string sourceText)
    {
        foreach (Match match in LocalReference().Matches(sourceText))
        {
            var name = match.Groups["dot"].Success ? match.Groups["dot"].Value : match.Groups["index"].Value;

            // "local.a-1" is the local "a" minus one, but "a-1" is also a legal name: accept every prefix.
            while (name.Length > 0)
            {
                if (_expressions.ContainsKey(name))
                {
                    yield return name;
                }

                var dash = name.LastIndexOf('-');
                name = dash < 0 ? string.Empty : name[..dash];
            }
        }
    }

    /// <summary>
    /// Evaluates the named locals and everything they depend on, and returns the requested ones as
    /// an object value for binding to <c>local</c>.
    /// </summary>
    public HclValue Resolve(IEnumerable<string> names, HclValue variables, HclEvaluator evaluator)
    {
        var memo = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            result[name] = Resolve(name, variables, evaluator, memo, visiting);
        }

        return HclValue.FromObject(result);
    }

    private HclValue Resolve(
        string name,
        HclValue variables,
        HclEvaluator evaluator,
        Dictionary<string, HclValue> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(name, out var known))
        {
            return known;
        }

        if (!visiting.Add(name))
        {
            return HclValue.Unknown($"local.{name} (circular reference)");
        }

        var scope = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        foreach (var dependency in _dependencies[name])
        {
            scope[dependency] = Resolve(dependency, variables, evaluator, memo, visiting);
        }

        var context = new HclEvaluationContext();
        context.SetVariable("var", variables);
        context.SetVariable("local", HclValue.FromObject(scope));

        HclValue value;
        try
        {
            value = evaluator.Evaluate(_expressions[name], context);
        }
        catch (Exception)
        {
            value = HclValue.Unknown($"local.{name}");
        }

        visiting.Remove(name);
        memo[name] = value;

        return value;
    }

    [GeneratedRegex(@"\blocal(?:\.(?<dot>[A-Za-z_][A-Za-z0-9_-]*)|\[\s*""(?<index>[^""]+)""\s*\])")]
    private static partial Regex LocalReference();
}
