namespace TerraformDotnet.Naming;

/// <summary>How a <see cref="NameFormat"/> writes the names it generates.</summary>
public enum NameStyle
{
    /// <summary>Lowercase words separated by underscores: <c>web_app</c>.</summary>
    SnakeCase,

    /// <summary>Words in the case they are given in, separated by underscores: <c>Web_App</c>.</summary>
    MixedSnakeCase,

    /// <summary>Lowercase words separated by hyphens: <c>web-app</c>.</summary>
    KebabCase,
}
