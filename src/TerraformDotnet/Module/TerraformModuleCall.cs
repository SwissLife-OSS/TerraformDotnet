using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a <c>module "name" { ... }</c> call declared by a Terraform module.
/// A module call refers to a child module; it does not represent the loaded child module itself.
/// </summary>
public sealed class TerraformModuleCall
{
    internal TerraformModuleCall(
        string name,
        HclExpression source,
        HclBody body,
        IReadOnlyDictionary<string, HclExpression> arguments,
        HclExpression? version,
        HclExpression? count,
        HclExpression? forEach,
        HclExpression? dependsOn,
        HclExpression? providers,
        HclExpression? ignoreNestedDeprecations)
    {
        Name = name;
        Source = source;
        Body = body;
        Arguments = arguments;
        Version = version;
        Count = count;
        ForEach = forEach;
        DependsOn = dependsOn;
        Providers = providers;
        IgnoreNestedDeprecations = ignoreNestedDeprecations;
    }

    /// <summary>Gets the local name used to address the module call.</summary>
    public string Name { get; }

    /// <summary>Gets the expression that identifies the child module source.</summary>
    public HclExpression Source { get; }

    /// <summary>Gets the registry module version constraint expression, if specified.</summary>
    public HclExpression? Version { get; }

    /// <summary>Gets the <c>count</c> meta-argument expression, if specified.</summary>
    public HclExpression? Count { get; }

    /// <summary>Gets the <c>for_each</c> meta-argument expression, if specified.</summary>
    public HclExpression? ForEach { get; }

    /// <summary>Gets the <c>depends_on</c> meta-argument expression, if specified.</summary>
    public HclExpression? DependsOn { get; }

    /// <summary>Gets the <c>providers</c> meta-argument expression, if specified.</summary>
    public HclExpression? Providers { get; }

    /// <summary>Gets the <c>ignore_nested_deprecations</c> argument expression, if specified.</summary>
    public HclExpression? IgnoreNestedDeprecations { get; }

    /// <summary>
    /// Gets the child module input arguments, excluding Terraform's built-in module arguments.
    /// </summary>
    public IReadOnlyDictionary<string, HclExpression> Arguments { get; }

    /// <summary>Gets the complete module call body for detailed inspection.</summary>
    public HclBody Body { get; }
}
