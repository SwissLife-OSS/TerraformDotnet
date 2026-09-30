using System.Collections.ObjectModel;
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
    /// Creates a scope that evaluates locals against one binding of <c>var</c>. Every local is
    /// evaluated at most once per scope, however many validations refer to it.
    /// </summary>
    /// <param name="variables">The object value bound to <c>var</c>.</param>
    /// <param name="evaluator">The evaluator that runs the local expressions.</param>
    public Scope CreateScope(HclValue variables, HclEvaluator evaluator) => new(this, variables, evaluator);

    /// <summary>
    /// The locals evaluated for one value of <c>var</c>. The scope memoizes results, so it must not be
    /// reused after the object bound to <c>var</c> has changed.
    /// </summary>
    internal sealed class Scope
    {
        private readonly ModuleLocals _owner;
        private readonly HclValue _variables;
        private readonly HclEvaluator _evaluator;
        private readonly Dictionary<string, HclValue> _memo = new(StringComparer.Ordinal);
        private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);

        internal Scope(ModuleLocals owner, HclValue variables, HclEvaluator evaluator)
        {
            _owner = owner;
            _variables = variables;
            _evaluator = evaluator;
        }

        /// <summary>
        /// Evaluates the named locals and everything they depend on, and returns the requested ones as
        /// an object value for binding to <c>local</c>.
        /// </summary>
        /// <param name="names">The names of the locals a condition refers to.</param>
        public HclValue Resolve(IReadOnlyList<string> names)
        {
            var result = new Dictionary<string, HclValue>(names.Count, StringComparer.Ordinal);
            foreach (var name in names)
            {
                result[name] = Resolve(name);
            }

            return Wrap(result);
        }

        // The dictionary is created for this value alone, so it can back the value without a copy.
        private static HclValue Wrap(Dictionary<string, HclValue> entries)
            => HclValue.WrapObject(new ReadOnlyDictionary<string, HclValue>(entries));

        private HclValue Resolve(string name)
        {
            if (_memo.TryGetValue(name, out var known))
            {
                return known;
            }

            if (!_visiting.Add(name))
            {
                return HclValue.Unknown($"local.{name} (circular reference)");
            }

            var dependencies = _owner._dependencies[name];
            var scope = new Dictionary<string, HclValue>(dependencies.Length, StringComparer.Ordinal);
            foreach (var dependency in dependencies)
            {
                scope[dependency] = Resolve(dependency);
            }

            var context = new HclEvaluationContext();
            context.SetVariable("var", _variables);
            context.SetVariable("local", Wrap(scope));

            HclValue value;
            try
            {
                value = _evaluator.Evaluate(_owner._expressions[name], context);
            }
            catch (Exception)
            {
                value = HclValue.Unknown($"local.{name}");
            }

            _visiting.Remove(name);
            _memo[name] = value;

            return value;
        }
    }

    [GeneratedRegex(@"\blocal(?:\.(?<dot>[A-Za-z_][A-Za-z0-9_-]*)|\[\s*""(?<index>[^""]+)""\s*\])")]
    private static partial Regex LocalReference();
}
