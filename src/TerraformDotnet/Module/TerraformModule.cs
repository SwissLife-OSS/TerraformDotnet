using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Module;

/// <summary>
/// Represents a parsed Terraform module — the merged result of all <c>.tf</c> files in a directory.
/// Provides access to variables, outputs, resources, data sources, locals, and provider requirements.
/// <example>
/// <code>
/// var module = TerraformModule.LoadFromDirectory("/path/to/module");
/// Console.WriteLine($"Required: {module.RequiredVariables.Count}");
/// Console.WriteLine($"Optional: {module.OptionalVariables.Count}");
/// </code>
/// </example>
/// </summary>
public sealed class TerraformModule
{
    private IReadOnlyList<TerraformVariable>? _requiredVariables;
    private IReadOnlyList<TerraformVariable>? _optionalVariables;

    private TerraformModule(
        IReadOnlyList<TerraformVariable> variables,
        IReadOnlyList<TerraformOutput> outputs,
        IReadOnlyList<TerraformResource> resources,
        IReadOnlyList<TerraformDataSource> dataSources,
        IReadOnlyList<TerraformLocal> locals,
        IReadOnlyList<TerraformProviderRequirement> providerRequirements,
        string? requiredTerraformVersion)
    {
        Variables = variables;
        Outputs = outputs;
        Resources = resources;
        DataSources = dataSources;
        Locals = locals;
        ProviderRequirements = providerRequirements;
        RequiredTerraformVersion = requiredTerraformVersion;
    }

    /// <summary>All variable declarations in the module.</summary>
    public IReadOnlyList<TerraformVariable> Variables { get; }

    /// <summary>Variables without a default value (required by Terraform convention).</summary>
    public IReadOnlyList<TerraformVariable> RequiredVariables =>
        _requiredVariables ??= Variables.Where(v => v.IsRequired).ToList();

    /// <summary>Variables with a default value (optional by Terraform convention).</summary>
    public IReadOnlyList<TerraformVariable> OptionalVariables =>
        _optionalVariables ??= Variables.Where(v => v.IsOptional).ToList();

    /// <summary>All output declarations in the module.</summary>
    public IReadOnlyList<TerraformOutput> Outputs { get; }

    /// <summary>All resource blocks in the module.</summary>
    public IReadOnlyList<TerraformResource> Resources { get; }

    /// <summary>All data source blocks in the module.</summary>
    public IReadOnlyList<TerraformDataSource> DataSources { get; }

    /// <summary>All local values in the module.</summary>
    public IReadOnlyList<TerraformLocal> Locals { get; }

    /// <summary>Provider requirements from <c>terraform { required_providers { } }</c>.</summary>
    public IReadOnlyList<TerraformProviderRequirement> ProviderRequirements { get; }

    /// <summary>The <c>required_version</c> constraint from the <c>terraform</c> block, if any.</summary>
    public string? RequiredTerraformVersion { get; }

    /// <summary>
    /// Loads a module from a directory containing <c>.tf</c> files.
    /// All <c>.tf</c> files in the directory are parsed and merged (non-recursive).
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing <c>.tf</c> files.</param>
    /// <exception cref="DirectoryNotFoundException">When the directory does not exist.</exception>
    public static TerraformModule LoadFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Module directory not found: '{directoryPath}'.");
        }

        var tfFiles = Directory.GetFiles(directoryPath, "*.tf");
        var hclFiles = new List<HclFile>(tfFiles.Length);

        foreach (var filePath in tfFiles)
        {
            var bytes = File.ReadAllBytes(filePath);
            hclFiles.Add(HclFile.Load(bytes));
        }

        return LoadFromFiles(hclFiles);
    }

    /// <summary>
    /// Loads a module from multiple HCL file contents (e.g. fetched from a git repository).
    /// Each element represents one <c>.tf</c> file's raw bytes.
    /// </summary>
    /// <param name="fileContents">The raw byte contents of each <c>.tf</c> file.</param>
    public static TerraformModule LoadFromContents(IEnumerable<byte[]> fileContents)
    {
        var hclFiles = fileContents.Select(bytes => HclFile.Load(bytes));

        return LoadFromFiles(hclFiles);
    }

    /// <summary>
    /// Loads a module from pre-parsed HCL files.
    /// </summary>
    /// <param name="files">The pre-parsed HCL files.</param>
    public static TerraformModule LoadFromFiles(IEnumerable<HclFile> files)
    {
        var variables = new List<TerraformVariable>();
        var outputs = new List<TerraformOutput>();
        var resources = new List<TerraformResource>();
        var dataSources = new List<TerraformDataSource>();
        var locals = new List<TerraformLocal>();
        var providerRequirements = new List<TerraformProviderRequirement>();
        string? requiredVersion = null;

        foreach (var file in files)
        {
            variables.AddRange(TerraformModuleLoader.ExtractVariables(file));
            outputs.AddRange(TerraformModuleLoader.ExtractOutputs(file));
            resources.AddRange(TerraformModuleLoader.ExtractResources(file));
            dataSources.AddRange(TerraformModuleLoader.ExtractDataSources(file));
            locals.AddRange(TerraformModuleLoader.ExtractLocals(file));
            providerRequirements.AddRange(TerraformModuleLoader.ExtractProviderRequirements(file));

            var ver = TerraformModuleLoader.ExtractRequiredVersion(file);

            if (ver is not null)
            {
                requiredVersion = ver;
            }
        }

        return new TerraformModule(
            variables, outputs, resources, dataSources,
            locals, providerRequirements, requiredVersion);
    }

    /// <summary>
    /// Loads a module from a single HCL content span (useful for testing).
    /// </summary>
    /// <param name="hclContent">The HCL content bytes.</param>
    public static TerraformModule LoadFromContent(ReadOnlySpan<byte> hclContent)
    {
        var file = HclFile.Load(hclContent);

        return LoadFromFiles([file]);
    }
}
