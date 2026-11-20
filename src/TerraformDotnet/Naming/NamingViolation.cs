namespace TerraformDotnet.Naming;

/// <summary>A name that does not have the format its <see cref="TerraformNamingConvention"/> requires.</summary>
/// <param name="Kind">The kind of the name.</param>
/// <param name="Name">The name.</param>
/// <param name="Format">The format the name must have.</param>
public sealed record NamingViolation(TerraformNameKind Kind, string Name, NameFormat Format)
{
    /// <summary>Gets the type of the resource or data source the name belongs to; <c>null</c> for other kinds.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// Gets the one-based line of the block (or the <c>locals</c> entry) in its file; <c>null</c>
    /// when the position is not known.
    /// </summary>
    public int? Line { get; init; }

    /// <summary>
    /// Gets the Terraform address of the named object: <c>module.web</c>, <c>aws_instance.web</c>,
    /// <c>data.aws_ami.web</c>, <c>var.web</c>, <c>output.web</c>, <c>local.web</c> or <c>check.web</c>.
    /// </summary>
    public string Address =>
        Kind switch
        {
            TerraformNameKind.Resource => $"{Type}.{Name}",
            TerraformNameKind.Data => $"data.{Type}.{Name}",
            TerraformNameKind.Module => $"module.{Name}",
            TerraformNameKind.Variable => $"var.{Name}",
            TerraformNameKind.Output => $"output.{Name}",
            TerraformNameKind.Local => $"local.{Name}",
            _ => $"check.{Name}",
        };

    /// <summary>
    /// Gets the message tflint reports for the name, for example
    /// <c>module name `web_app` must match the following format: snake_case</c>.
    /// </summary>
    public string Message =>
        $"{KindName} name `{Name}` must match the following {(Format.IsNamed ? "format" : "RegExp")}: {Format.Name}";

    private string KindName =>
        Kind switch
        {
            TerraformNameKind.Resource => "resource",
            TerraformNameKind.Data => "data",
            TerraformNameKind.Module => "module",
            TerraformNameKind.Variable => "variable",
            TerraformNameKind.Output => "output",
            TerraformNameKind.Local => "local value",
            _ => "check",
        };
}
