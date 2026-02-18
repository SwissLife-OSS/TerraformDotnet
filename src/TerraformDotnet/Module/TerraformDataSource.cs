using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a <c>data "type" "name" { ... }</c> block in a Terraform module.
/// <example>
/// <code>
/// // data "cloud_image" "latest" {
/// //   name   = "ubuntu-22.04"
/// //   region = var.region
/// // }
/// </code>
/// </example>
/// </summary>
public sealed class TerraformDataSource
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformDataSource"/>.
    /// </summary>
    /// <param name="type">The data source type (e.g. "cloud_image").</param>
    /// <param name="name">The local data source name (e.g. "latest").</param>
    /// <param name="body">The full body AST.</param>
    /// <param name="count">The <c>count</c> expression, if any.</param>
    /// <param name="forEach">The <c>for_each</c> expression, if any.</param>
    internal TerraformDataSource(
        string type,
        string name,
        HclBody body,
        HclExpression? count = null,
        HclExpression? forEach = null)
    {
        Type = type;
        Name = name;
        Body = body;
        Count = count;
        ForEach = forEach;
    }

    /// <summary>Gets the data source type (e.g. "cloud_image").</summary>
    public string Type { get; }

    /// <summary>Gets the local data source name (e.g. "latest").</summary>
    public string Name { get; }

    /// <summary>Gets the full body AST.</summary>
    public HclBody Body { get; }

    /// <summary>Gets the <c>count</c> expression, if any.</summary>
    public HclExpression? Count { get; }

    /// <summary>Gets the <c>for_each</c> expression, if any.</summary>
    public HclExpression? ForEach { get; }
}
