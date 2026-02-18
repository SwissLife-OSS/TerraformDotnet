using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a <c>resource "type" "name" { ... }</c> block in a Terraform module.
/// <example>
/// <code>
/// // resource "cloud_instance" "main" {
/// //   name   = local.instance_name
/// //   region = var.region
/// // }
/// </code>
/// </example>
/// </summary>
public sealed class TerraformResource
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformResource"/>.
    /// </summary>
    /// <param name="type">The resource type (e.g. "cloud_instance").</param>
    /// <param name="name">The local resource name (e.g. "main").</param>
    /// <param name="body">The full body AST for deep inspection.</param>
    /// <param name="count">The <c>count</c> expression, if any.</param>
    /// <param name="forEach">The <c>for_each</c> expression, if any.</param>
    /// <param name="dependsOn">The explicit <c>depends_on</c> list.</param>
    /// <param name="provider">The <c>provider</c> reference, if any.</param>
    internal TerraformResource(
        string type,
        string name,
        HclBody body,
        HclExpression? count = null,
        HclExpression? forEach = null,
        IReadOnlyList<string>? dependsOn = null,
        string? provider = null)
    {
        Type = type;
        Name = name;
        Body = body;
        Count = count;
        ForEach = forEach;
        DependsOn = dependsOn;
        Provider = provider;
    }

    /// <summary>Gets the resource type (e.g. "cloud_instance").</summary>
    public string Type { get; }

    /// <summary>Gets the local resource name (e.g. "main").</summary>
    public string Name { get; }

    /// <summary>Gets the full body AST for deep inspection.</summary>
    public HclBody Body { get; }

    /// <summary>Gets the <c>count</c> expression, if any.</summary>
    public HclExpression? Count { get; }

    /// <summary>Gets the <c>for_each</c> expression, if any.</summary>
    public HclExpression? ForEach { get; }

    /// <summary>Gets the explicit <c>depends_on</c> list.</summary>
    public IReadOnlyList<string>? DependsOn { get; }

    /// <summary>Gets the <c>provider</c> reference, if any.</summary>
    public string? Provider { get; }
}
