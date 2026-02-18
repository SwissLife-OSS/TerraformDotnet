using TerraformDotnet.Module;

namespace TerraformDotnet.Emit;

/// <summary>
/// Fluent builder for constructing a <see cref="ModuleCall"/>.
/// When constructed with a <see cref="TerraformModule"/>, it validates that all
/// required variables are set and can auto-generate commented-out optional variables.
/// <example>
/// <code>
/// var call = new ModuleCallBuilder("my-app", module)
///     .Source("git::https://example.com/modules/app?ref=v1.0")
///     .FillRequired(name => $"var.{name}")
///     .IncludeOptionalComments(true)
///     .Build();
/// </code>
/// </example>
/// </summary>
public sealed class ModuleCallBuilder
{
    private readonly string _name;
    private readonly TerraformModule? _module;
    private readonly Dictionary<string, string> _arguments = new();
    private string? _source;
    private string? _version;
    private string? _count;
    private string? _forEach;
    private List<string>? _dependsOn;
    private Dictionary<string, string>? _providers;
    private bool _includeOptionalComments;

    /// <summary>
    /// Initializes a new builder without a source module. No required-variable validation is performed.
    /// </summary>
    /// <param name="moduleName">The module call name (used in <c>module "name" { }</c>).</param>
    public ModuleCallBuilder(string moduleName)
    {
        _name = moduleName;
    }

    /// <summary>
    /// Initializes a new builder with a source module. Build() will validate that
    /// all required variables are set.
    /// </summary>
    /// <param name="moduleName">The module call name.</param>
    /// <param name="module">The source module for validation and metadata.</param>
    public ModuleCallBuilder(string moduleName, TerraformModule module)
    {
        _name = moduleName;
        _module = module;
    }

    /// <summary>Sets the module source (required).</summary>
    /// <param name="source">The source path or URL.</param>
    public ModuleCallBuilder Source(string source)
    {
        _source = source;

        return this;
    }

    /// <summary>Sets the module version constraint (for registry sources).</summary>
    /// <param name="version">The version constraint string.</param>
    public ModuleCallBuilder Version(string version)
    {
        _version = version;

        return this;
    }

    /// <summary>Sets a variable to a raw HCL expression string.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="hclExpression">The raw HCL expression (e.g. "var.name").</param>
    public ModuleCallBuilder Set(string name, string hclExpression)
    {
        _arguments[name] = hclExpression;

        return this;
    }

    /// <summary>Sets a variable to a string literal.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The string value (will be quoted).</param>
    public ModuleCallBuilder SetLiteral(string name, string value)
    {
        _arguments[name] = $"\"{value}\"";

        return this;
    }

    /// <summary>Sets a variable to a numeric literal.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The numeric value.</param>
    public ModuleCallBuilder SetLiteral(string name, int value)
    {
        _arguments[name] = value.ToString();

        return this;
    }

    /// <summary>Sets a variable to a boolean literal.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The boolean value.</param>
    public ModuleCallBuilder SetLiteral(string name, bool value)
    {
        _arguments[name] = value ? "true" : "false";

        return this;
    }

    /// <summary>
    /// Auto-fills all required variables using a naming convention.
    /// Variables already explicitly set are skipped.
    /// <example>
    /// <code>
    /// builder.FillRequired(name => $"var.{name}");
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="expressionFactory">A function that generates the HCL expression from a variable name.</param>
    /// <exception cref="InvalidOperationException">When the builder was created without a module.</exception>
    public ModuleCallBuilder FillRequired(Func<string, string> expressionFactory)
    {
        if (_module is null)
        {
            throw new InvalidOperationException(
                "FillRequired requires a TerraformModule. Use the constructor that accepts a module.");
        }

        foreach (var variable in _module.RequiredVariables)
        {
            if (!_arguments.ContainsKey(variable.Name))
            {
                _arguments[variable.Name] = expressionFactory(variable.Name);
            }
        }

        return this;
    }

    /// <summary>Sets the <c>count</c> meta-argument.</summary>
    /// <param name="hclExpression">The count expression.</param>
    public ModuleCallBuilder Count(string hclExpression)
    {
        _count = hclExpression;

        return this;
    }

    /// <summary>Sets the <c>for_each</c> meta-argument.</summary>
    /// <param name="hclExpression">The for_each expression.</param>
    public ModuleCallBuilder ForEach(string hclExpression)
    {
        _forEach = hclExpression;

        return this;
    }

    /// <summary>Sets the <c>depends_on</c> meta-argument.</summary>
    /// <param name="dependencies">The dependency references.</param>
    public ModuleCallBuilder DependsOn(params string[] dependencies)
    {
        _dependsOn = [.. dependencies];

        return this;
    }

    /// <summary>Sets the <c>providers</c> meta-argument.</summary>
    /// <param name="providerMap">Mapping of provider names.</param>
    public ModuleCallBuilder Providers(IDictionary<string, string> providerMap)
    {
        _providers = new Dictionary<string, string>(providerMap);

        return this;
    }

    /// <summary>
    /// Controls whether optional variables appear as commented-out lines in the emitted module block.
    /// Default: <c>false</c>.
    /// </summary>
    /// <param name="include">Whether to include optional variable comments.</param>
    public ModuleCallBuilder IncludeOptionalComments(bool include)
    {
        _includeOptionalComments = include;

        return this;
    }

    /// <summary>
    /// Builds the immutable <see cref="ModuleCall"/> result.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When source is not set, or when required variables are missing (module-aware mode).
    /// </exception>
    public ModuleCall Build()
    {
        if (_source is null)
        {
            throw new InvalidOperationException("Module source must be set. Call Source() before Build().");
        }

        if (_module is not null)
        {
            var missing = _module.RequiredVariables
                .Where(v => !_arguments.ContainsKey(v.Name))
                .Select(v => v.Name)
                .ToList();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Missing required variables: {string.Join(", ", missing)}. " +
                    "Set them with Set(), SetLiteral(), or FillRequired().");
            }
        }

        var commentedOptionals = BuildCommentedOptionals();

        return new ModuleCall(
            _name,
            _source,
            _version,
            new Dictionary<string, string>(_arguments),
            commentedOptionals,
            _module,
            _count,
            _forEach,
            _dependsOn,
            _providers);
    }

    private List<CommentedVariable> BuildCommentedOptionals()
    {
        if (!_includeOptionalComments || _module is null)
        {
            return [];
        }

        var commented = new List<CommentedVariable>();

        foreach (var variable in _module.OptionalVariables)
        {
            if (!_arguments.ContainsKey(variable.Name))
            {
                commented.Add(new CommentedVariable(
                    variable.Name,
                    variable.Description,
                    $"var.{variable.Name}"));
            }
        }

        return commented;
    }
}
