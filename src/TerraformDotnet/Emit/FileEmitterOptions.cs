namespace TerraformDotnet.Emit;

/// <summary>
/// Controls which files to generate and their names when using
/// <see cref="ModuleCallEmitter.WriteTo(string, FileEmitterOptions)"/>.
/// <example>
/// <code>
/// var options = new FileEmitterOptions
/// {
///     ModuleFileName = "resources-app.tf",
///     VariablesFileName = "variables-app.tf",
///     InputFiles = new Dictionary&lt;string, IDictionary&lt;string, InputValue&gt;&gt;
///     {
///         ["input-dev.tfvars"] = devValues,
///         ["input-prod.tfvars"] = prodValues,
///     },
/// };
/// </code>
/// </example>
/// </summary>
public sealed class FileEmitterOptions
{
    /// <summary>
    /// Name of the module block file. Default: <c>"resources-{moduleName}.tf"</c>.
    /// </summary>
    public string? ModuleFileName { get; init; }

    /// <summary>
    /// Name of the variables file. <c>null</c> to skip. Default: <c>null</c>.
    /// </summary>
    public string? VariablesFileName { get; init; }

    /// <summary>
    /// Input files to generate — one per environment.
    /// Key = file name (e.g. "input-dev.tfvars"), Value = variable values.
    /// <c>null</c> to skip.
    /// </summary>
    public IDictionary<string, IDictionary<string, InputValue>>? InputFiles { get; init; }
}
