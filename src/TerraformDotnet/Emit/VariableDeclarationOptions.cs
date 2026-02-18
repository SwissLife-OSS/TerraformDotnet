namespace TerraformDotnet.Emit;

/// <summary>
/// Controls how pass-through variable declarations are formatted.
/// </summary>
public sealed class VariableDeclarationOptions
{
    /// <summary>Include the original type constraint. Default: <c>false</c>.</summary>
    public bool IncludeType { get; init; }

    /// <summary>Include the original description. Default: <c>false</c>.</summary>
    public bool IncludeDescription { get; init; }

    /// <summary>Include the original default value. Default: <c>false</c>.</summary>
    public bool IncludeDefault { get; init; }

    /// <summary>Variable names to exclude from the output. Default: empty.</summary>
    public ISet<string> ExcludeVariables { get; init; } = new HashSet<string>();
}
