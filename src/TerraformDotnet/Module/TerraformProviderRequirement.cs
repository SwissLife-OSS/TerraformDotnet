namespace TerraformDotnet.Module;

/// <summary>
/// Represents a <c>required_providers</c> entry inside a <c>terraform { ... }</c> block.
/// <example>
/// <code>
/// // terraform {
/// //   required_providers {
/// //     cloud = {
/// //       source  = "registry.example.com/example/cloud"
/// //       version = ">= 2.0"
/// //     }
/// //   }
/// // }
/// </code>
/// </example>
/// </summary>
public sealed class TerraformProviderRequirement
{
    /// <summary>
    /// Initializes a new instance of <see cref="TerraformProviderRequirement"/>.
    /// </summary>
    /// <param name="name">The provider local name (e.g. "cloud").</param>
    /// <param name="source">The provider source (e.g. "registry.example.com/example/cloud").</param>
    /// <param name="version">The version constraint (e.g. ">= 2.0").</param>
    internal TerraformProviderRequirement(string name, string? source = null, string? version = null)
    {
        Name = name;
        Source = source;
        Version = version;
    }

    /// <summary>Gets the provider local name (e.g. "cloud").</summary>
    public string Name { get; }

    /// <summary>Gets the provider source (e.g. "registry.example.com/example/cloud").</summary>
    public string? Source { get; }

    /// <summary>Gets the version constraint (e.g. ">= 2.0").</summary>
    public string? Version { get; }
}
