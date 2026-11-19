using System.Text;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;

namespace TerraformDotnet.Naming;

/// <summary>
/// The naming convention of a Terraform configuration: one <see cref="NameFormat"/> per kind of
/// name. It generates names, checks them, and can be read from the tflint rule
/// <c>terraform_naming_convention</c>, so that generated code passes the same lint.
/// <example>
/// <code>
/// // Terraform style: snake_case for every kind.
/// var convention = TerraformNamingConvention.Default;
///
/// // Hyphens for resources, modules and data sources.
/// var hyphens = NameFormat.Custom("^[a-z0-9-]+$");
/// convention = convention with { Resource = hyphens, Module = hyphens, Data = hyphens };
///
/// convention.Format(TerraformNameKind.Module, "dns", "web_app");   // "dns-web-app"
/// convention.Format(TerraformNameKind.Variable, "dns", "web-app"); // "dns_web_app"
/// </code>
/// </example>
/// </summary>
public sealed record TerraformNamingConvention
{
    /// <summary>
    /// Gets the Terraform style, which is also the default of tflint: <see cref="NameFormat.SnakeCase"/>
    /// for every kind of name.
    /// </summary>
    public static TerraformNamingConvention Default { get; } = new();

    /// <summary>Gets the format of resource names.</summary>
    public NameFormat Resource { get; init; } = NameFormat.SnakeCase;

    /// <summary>Gets the format of data source names.</summary>
    public NameFormat Data { get; init; } = NameFormat.SnakeCase;

    /// <summary>Gets the format of module call names.</summary>
    public NameFormat Module { get; init; } = NameFormat.SnakeCase;

    /// <summary>Gets the format of input variable names.</summary>
    public NameFormat Variable { get; init; } = NameFormat.SnakeCase;

    /// <summary>Gets the format of output value names.</summary>
    public NameFormat Output { get; init; } = NameFormat.SnakeCase;

    /// <summary>Gets the format of local value names.</summary>
    public NameFormat Local { get; init; } = NameFormat.SnakeCase;

    /// <summary>Gets the format of check names.</summary>
    public NameFormat Check { get; init; } = NameFormat.SnakeCase;

    /// <summary>Creates a convention that uses <paramref name="format"/> for every kind of name.</summary>
    /// <param name="format">The format of every kind of name.</param>
    public static TerraformNamingConvention Uniform(NameFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return new TerraformNamingConvention
        {
            Resource = format,
            Data = format,
            Module = format,
            Variable = format,
            Output = format,
            Local = format,
            Check = format,
        };
    }

    /// <summary>
    /// Reads the convention from a tflint configuration (<c>.tflint.hcl</c>).
    /// </summary>
    /// <param name="hcl">The content of the tflint configuration.</param>
    /// <param name="fallback">
    /// The convention whose styles are used to generate names where the rule leaves the style open:
    /// for a kind whose format is <c>none</c>, and for a custom pattern that accepts the style.
    /// Defaults to <see cref="Default"/>. It has no effect on which names are valid.
    /// </param>
    /// <returns>
    /// The convention of the rule <c>terraform_naming_convention</c>, or <c>null</c> when the
    /// configuration does not enable the rule, so tflint checks no names. The rule is enabled by a
    /// <c>rule</c> block with <c>enabled = true</c>, or by <c>preset = "all"</c> of the
    /// <c>terraform</c> plugin without <c>disabled_by_default</c>. Rules enabled on the tflint
    /// command line are not known here.
    /// </returns>
    /// <exception cref="HclSyntaxException">The content is not valid HCL.</exception>
    /// <exception cref="FormatException">
    /// The rule is configured in a way tflint rejects: an unknown format, an invalid regular
    /// expression, or a value of the wrong type.
    /// </exception>
    public static TerraformNamingConvention? FromTflintConfig(
        string hcl,
        TerraformNamingConvention? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(hcl);

        return FromTflintConfig(HclFile.Load(Encoding.UTF8.GetBytes(hcl)), fallback);
    }

    /// <summary>
    /// Reads the convention from a parsed tflint configuration (<c>.tflint.hcl</c>).
    /// See <see cref="FromTflintConfig(string, TerraformNamingConvention?)"/>.
    /// </summary>
    /// <param name="config">The parsed tflint configuration.</param>
    /// <param name="fallback">The convention whose styles are used where the rule leaves the style open.</param>
    /// <returns>The convention of the rule, or <c>null</c> when the configuration does not enable it.</returns>
    /// <exception cref="FormatException">The rule is configured in a way tflint rejects.</exception>
    public static TerraformNamingConvention? FromTflintConfig(
        HclFile config,
        TerraformNamingConvention? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        return TflintNamingConfig.Read(config, fallback ?? Default);
    }

    /// <summary>
    /// Checks whether a name can be used as a Terraform name at all, whatever the convention: a
    /// letter or underscore followed by letters, digits, underscores and hyphens (ASCII).
    /// </summary>
    /// <param name="name">The name to check.</param>
    public static bool IsIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return NameFormat.IsIdentifier(name);
    }

    /// <summary>Gets the format of a kind of name.</summary>
    /// <param name="kind">The kind of name.</param>
    public NameFormat GetFormat(TerraformNameKind kind) =>
        kind switch
        {
            TerraformNameKind.Resource => Resource,
            TerraformNameKind.Data => Data,
            TerraformNameKind.Module => Module,
            TerraformNameKind.Variable => Variable,
            TerraformNameKind.Output => Output,
            TerraformNameKind.Local => Local,
            TerraformNameKind.Check => Check,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>
    /// Builds a name of a kind from parts. Names that already conform are returned as they are.
    /// See <see cref="NameFormat.Format"/>.
    /// </summary>
    /// <param name="kind">The kind of name.</param>
    /// <param name="parts">The parts of the name. <c>null</c> and empty parts are skipped.</param>
    /// <exception cref="ArgumentException">The parts contain no letter or digit.</exception>
    public string Format(TerraformNameKind kind, params string?[] parts) => GetFormat(kind).Format(parts);

    /// <summary>
    /// Checks whether a name has the format of its kind, like tflint does. A kind that is not checked
    /// accepts every name.
    /// </summary>
    /// <param name="kind">The kind of name.</param>
    /// <param name="name">The name to check.</param>
    public bool IsValid(TerraformNameKind kind, string name) => GetFormat(kind).IsMatch(name);

    /// <summary>
    /// Finds the names in a file that do not follow the convention. It checks the blocks tflint
    /// checks: <c>resource</c>, <c>data</c> (also inside <c>check</c> blocks), <c>module</c>,
    /// <c>variable</c>, <c>output</c>, <c>check</c> and the entries of <c>locals</c>.
    /// </summary>
    /// <param name="file">The parsed Terraform file.</param>
    /// <returns>The violations in the order of the blocks in the file.</returns>
    public IReadOnlyList<NamingViolation> Validate(HclFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var violations = new List<NamingViolation>();

        foreach (HclBlock block in file.Body.Blocks)
        {
            switch (block.Type)
            {
                case "resource" when block.Labels.Count == 2:
                    AddViolation(violations, TerraformNameKind.Resource, block.Labels[1], block.Labels[0], block);
                    break;

                case "data" when block.Labels.Count == 2:
                    AddViolation(violations, TerraformNameKind.Data, block.Labels[1], block.Labels[0], block);
                    break;

                case "module" when block.Labels.Count == 1:
                    AddViolation(violations, TerraformNameKind.Module, block.Labels[0], null, block);
                    break;

                case "variable" when block.Labels.Count == 1:
                    AddViolation(violations, TerraformNameKind.Variable, block.Labels[0], null, block);
                    break;

                case "output" when block.Labels.Count == 1:
                    AddViolation(violations, TerraformNameKind.Output, block.Labels[0], null, block);
                    break;

                case "check" when block.Labels.Count == 1:
                    AddViolation(violations, TerraformNameKind.Check, block.Labels[0], null, block);

                    foreach (HclBlock nested in block.Body.Blocks)
                    {
                        if (nested.Type == "data" && nested.Labels.Count == 2)
                        {
                            AddViolation(violations, TerraformNameKind.Data, nested.Labels[1], nested.Labels[0], nested);
                        }
                    }

                    break;

                case "locals" when block.Labels.Count == 0:
                    foreach (HclAttribute local in block.Body.Attributes)
                    {
                        AddViolation(violations, TerraformNameKind.Local, local.Name, null, local);
                    }

                    break;
            }
        }

        return violations;
    }

    /// <summary>
    /// Finds the names in a loaded module that do not follow the convention: resources, data
    /// sources, module calls, variables, outputs and locals. A <see cref="TerraformModule"/> does not
    /// hold <c>check</c> blocks and positions; use <see cref="Validate(HclFile)"/> for those.
    /// </summary>
    /// <param name="module">The loaded module.</param>
    public IReadOnlyList<NamingViolation> Validate(TerraformModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var violations = new List<NamingViolation>();

        foreach (TerraformResource resource in module.Resources)
        {
            AddViolation(violations, TerraformNameKind.Resource, resource.Name, resource.Type, null);
        }

        foreach (TerraformDataSource dataSource in module.DataSources)
        {
            AddViolation(violations, TerraformNameKind.Data, dataSource.Name, dataSource.Type, null);
        }

        foreach (TerraformModuleCall call in module.ModuleCalls)
        {
            AddViolation(violations, TerraformNameKind.Module, call.Name, null, null);
        }

        foreach (TerraformVariable variable in module.Variables)
        {
            AddViolation(violations, TerraformNameKind.Variable, variable.Name, null, null);
        }

        foreach (TerraformOutput output in module.Outputs)
        {
            AddViolation(violations, TerraformNameKind.Output, output.Name, null, null);
        }

        foreach (TerraformLocal local in module.Locals)
        {
            AddViolation(violations, TerraformNameKind.Local, local.Name, null, null);
        }

        return violations;
    }

    private void AddViolation(
        List<NamingViolation> violations,
        TerraformNameKind kind,
        string name,
        string? type,
        HclNode? node)
    {
        NameFormat format = GetFormat(kind);

        if (format.IsMatch(name))
        {
            return;
        }

        violations.Add(new NamingViolation(kind, name, format)
        {
            Type = type,
            Line = node is { Start.Line: > 0 } ? node.Start.Line : null,
        });
    }
}
