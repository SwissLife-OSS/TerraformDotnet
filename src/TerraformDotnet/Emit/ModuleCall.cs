using TerraformDotnet.Module;

namespace TerraformDotnet.Emit;

/// <summary>
/// Immutable result of a <see cref="ModuleCallBuilder"/>. Represents all the data
/// needed to emit a Terraform module call block and associated files.
/// </summary>
public sealed class ModuleCall
{
    internal ModuleCall(
        string name,
        string source,
        string? version,
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyList<CommentedVariable> commentedOptionalVariables,
        TerraformModule? sourceModule,
        string? count,
        string? forEach,
        IReadOnlyList<string>? dependsOn,
        IReadOnlyDictionary<string, string>? providers)
    {
        Name = name;
        Source = source;
        Version = version;
        Arguments = arguments;
        CommentedOptionalVariables = commentedOptionalVariables;
        SourceModule = sourceModule;
        Count = count;
        ForEach = forEach;
        DependsOn = dependsOn;
        Providers = providers;
    }

    /// <summary>Gets the module call name.</summary>
    public string Name { get; }

    /// <summary>Gets the module source.</summary>
    public string Source { get; }

    /// <summary>Gets the module version constraint, if any.</summary>
    public string? Version { get; }

    /// <summary>Gets the argument name-to-expression mappings.</summary>
    public IReadOnlyDictionary<string, string> Arguments { get; }

    /// <summary>Gets the optional variables rendered as comments.</summary>
    internal IReadOnlyList<CommentedVariable> CommentedOptionalVariables { get; }

    /// <summary>Gets the source module used for validation and metadata, if any.</summary>
    public TerraformModule? SourceModule { get; }

    /// <summary>Gets the <c>count</c> expression, if any.</summary>
    public string? Count { get; }

    /// <summary>Gets the <c>for_each</c> expression, if any.</summary>
    public string? ForEach { get; }

    /// <summary>Gets the <c>depends_on</c> list, if any.</summary>
    public IReadOnlyList<string>? DependsOn { get; }

    /// <summary>Gets the <c>providers</c> mapping, if any.</summary>
    public IReadOnlyDictionary<string, string>? Providers { get; }
}
