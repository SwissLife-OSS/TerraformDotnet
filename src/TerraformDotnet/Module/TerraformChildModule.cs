using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a <c>module "name" { ... }</c> block in a Terraform module.
/// <example>
/// <code>
/// // module "reusable" {
/// //   source = "../../module_source"
/// //
/// //   name   = local.instance_name
/// //   region = var.region
/// // }
/// </code>
/// </example>
/// </summary>
public sealed class TerraformChildModule
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformChildModule"/>.
    /// </summary>
    /// <param name="name">The module name (e.g. "main").</param>
    /// <param name="source">The local or remote source of the module</param>
    /// <param name="body">The full body AST for deep inspection.</param>
    /// <param name="version">The version of registry-sourced modules</param>
    /// <param name="count">The <c>count</c> expression, if any.</param>
    /// <param name="forEach">The <c>for_each</c> expression, if any.</param>
    /// <param name="dependsOn">The explicit <c>depends_on</c> list.</param>
    internal TerraformChildModule(
        string name,
        HclExpression source,
        HclBody body,
        HclExpression? version = null,
        HclExpression? count = null,
        HclExpression? forEach = null,
        IReadOnlyList<string>? dependsOn = null)
    {
        Name = name;
        Source = source;
        Body = body;
        Version = version;
        Count = count;
        ForEach = forEach;
        DependsOn = dependsOn;
    }

    /// <summary>Gets the local resource name (e.g. "main").</summary>
    public string Name { get; }

    /// <summary>Gets the location source of the module (e.g. "../../../module_source").</summary>
    public HclExpression Source { get; }

    /// <summary>Gets the version of the module for registry hosted modules (e.g. "1.0.0").</summary>
    public HclExpression? Version { get; }

    /// <summary>Gets the full body AST for deep inspection.</summary>
    public HclBody Body { get; }

    /// <summary>Gets the <c>count</c> expression, if any.</summary>
    public HclExpression? Count { get; }

    /// <summary>Gets the <c>for_each</c> expression, if any.</summary>
    public HclExpression? ForEach { get; }

    /// <summary>Gets the explicit <c>depends_on</c> list.</summary>
    public IReadOnlyList<string>? DependsOn { get; }
}
