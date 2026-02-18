namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Holds variable bindings used during HCL expression evaluation.
/// Supports nested scopes via an optional parent context, enabling
/// block-level variable resolution (e.g. <c>for</c> expression iterators).
/// </summary>
/// <example>
/// <code>
/// var ctx = new HclEvaluationContext();
/// ctx.SetVariable("region", HclValue.FromString("us-east-1"));
///
/// var child = ctx.CreateChildScope();
/// child.SetVariable("item", HclValue.FromString("value"));
/// // child can see both "item" and "region"
/// </code>
/// </example>
public sealed class HclEvaluationContext
{
    private readonly Dictionary<string, HclValue> _variables = new(StringComparer.Ordinal);
    private readonly HclEvaluationContext? _parent;

    /// <summary>
    /// Initializes a new root evaluation context with no parent scope.
    /// </summary>
    public HclEvaluationContext()
    {
    }

    /// <summary>
    /// Initializes a new child evaluation context that inherits variable bindings
    /// from a parent scope.
    /// </summary>
    /// <param name="parent">The parent context to inherit from.</param>
    private HclEvaluationContext(HclEvaluationContext parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Gets the parent scope, or <c>null</c> if this is a root context.
    /// </summary>
    public HclEvaluationContext? Parent => _parent;

    /// <summary>
    /// Sets a variable binding in this scope.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The resolved value to bind.</param>
    public void SetVariable(string name, HclValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        _variables[name] = value;
    }

    /// <summary>
    /// Attempts to resolve a variable by name, searching this scope and all ancestor scopes.
    /// </summary>
    /// <param name="name">The variable name to look up.</param>
    /// <param name="value">When this method returns <c>true</c>, contains the resolved value.</param>
    /// <returns><c>true</c> if the variable was found; <c>false</c> otherwise.</returns>
    public bool TryGetVariable(string name, out HclValue value)
    {
        if (_variables.TryGetValue(name, out value!))
        {
            return true;
        }

        if (_parent is not null)
        {
            return _parent.TryGetVariable(name, out value);
        }

        value = default!;

        return false;
    }

    /// <summary>
    /// Creates a child scope that inherits all variable bindings from this context.
    /// New bindings in the child do not affect the parent.
    /// </summary>
    /// <returns>A new child <see cref="HclEvaluationContext"/>.</returns>
    public HclEvaluationContext CreateChildScope() => new(this);

    /// <summary>
    /// Gets the number of variables defined in this scope only (not including parent scopes).
    /// </summary>
    public int Count => _variables.Count;

    /// <summary>
    /// Returns <c>true</c> if a variable with the given name is defined in this scope or any ancestor scope.
    /// </summary>
    /// <param name="name">The variable name to check.</param>
    /// <returns><c>true</c> if the variable is defined; <c>false</c> otherwise.</returns>
    public bool ContainsVariable(string name) => TryGetVariable(name, out _);
}
