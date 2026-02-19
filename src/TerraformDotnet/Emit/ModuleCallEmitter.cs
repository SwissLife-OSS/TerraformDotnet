using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Writer;
using TerraformDotnet.Module;

namespace TerraformDotnet.Emit;

/// <summary>
/// Emits Terraform code from a <see cref="ModuleCall"/> — the module block,
/// pass-through variable declarations, and <c>.tfvars</c> input files.
/// <example>
/// <code>
/// var emitter = new ModuleCallEmitter(call);
/// string moduleBlock = emitter.EmitModuleBlock();
/// string variables = emitter.EmitVariableDeclarations();
/// string inputs = emitter.EmitInputValues(values);
/// </code>
/// </example>
/// </summary>
public sealed class ModuleCallEmitter
{
    private readonly ModuleCall _call;

    /// <summary>
    /// Initializes a new emitter for the given module call.
    /// </summary>
    /// <param name="call">The module call to emit.</param>
    public ModuleCallEmitter(ModuleCall call)
    {
        _call = call;
    }

    /// <summary>
    /// Emits the <c>module "name" { ... }</c> block as an HCL string.
    /// Arguments are aligned with padding matching <c>terraform fmt</c> conventions.
    /// </summary>
    public string EmitModuleBlock()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"module \"{_call.Name}\" {{");

        // Group 1: source + version
        var sourceGroup = new List<string> { "source" };

        if (_call.Version is not null)
        {
            sourceGroup.Add("version");
        }

        var sourcePadding = Utf8HclWriter.AlignAttributes(sourceGroup);

        // Source (always first)
        EmitAttribute(sb, "source", $"\"{_call.Source}\"", sourcePadding);

        // Version (if set, right after source)
        if (_call.Version is not null)
        {
            EmitAttribute(sb, "version", $"\"{_call.Version}\"", sourcePadding);
        }

        // Group 2: arguments
        if (_call.Arguments.Count > 0)
        {
            sb.AppendLine();

            var argPadding = Utf8HclWriter.AlignAttributes(_call.Arguments.Keys.ToList());

            foreach (var arg in _call.Arguments)
            {
                EmitAttribute(sb, arg.Key, arg.Value, argPadding);
            }
        }

        // Commented optional variables
        if (_call.CommentedOptionalVariables.Count > 0)
        {
            sb.AppendLine();

            var commentedNames = _call.CommentedOptionalVariables
                .Select(c => c.Name)
                .ToList();
            var commentedPadding = Utf8HclWriter.AlignAttributes(commentedNames);

            foreach (var commented in _call.CommentedOptionalVariables)
            {
                if (commented.Description is not null)
                {
                    sb.AppendLine($"  # (Optional) {commented.Description}");
                }

                var namePad = commentedPadding.TryGetValue(commented.Name, out var p)
                    ? new string(' ', p)
                    : "";
                sb.AppendLine($"  # {commented.Name}{namePad} = {commented.SuggestedExpression}");
            }
        }

        // Group 3: meta-arguments
        var metaNames = new List<string>();

        if (_call.Count is not null)
        {
            metaNames.Add("count");
        }

        if (_call.ForEach is not null)
        {
            metaNames.Add("for_each");
        }

        if (_call.DependsOn is not null)
        {
            metaNames.Add("depends_on");
        }

        if (_call.Providers is not null)
        {
            metaNames.Add("providers");
        }

        if (metaNames.Count > 0)
        {
            if (_call.Arguments.Count > 0 || _call.CommentedOptionalVariables.Count > 0)
            {
                sb.AppendLine();
            }

            var metaPadding = Utf8HclWriter.AlignAttributes(metaNames);

            if (_call.Count is not null)
            {
                EmitAttribute(sb, "count", _call.Count, metaPadding);
            }

            if (_call.ForEach is not null)
            {
                EmitAttribute(sb, "for_each", _call.ForEach, metaPadding);
            }

            if (_call.DependsOn is not null)
            {
                var deps = string.Join(", ", _call.DependsOn);
                EmitAttribute(sb, "depends_on", $"[{deps}]", metaPadding);
            }

            if (_call.Providers is not null)
            {
                var pairs = _call.Providers.Select(p => $"{p.Key} = {p.Value}");
                var providersBlock = $"{{ {string.Join(", ", pairs)} }}";
                EmitAttribute(sb, "providers", providersBlock, metaPadding);
            }
        }

        sb.Append('}');

        return sb.ToString();
    }

    /// <summary>
    /// Emits pass-through variable declarations for the calling module.
    /// These are the variables the caller needs to declare so they can forward values.
    /// </summary>
    /// <param name="options">Controls which attributes to include in declarations.</param>
    public string EmitVariableDeclarations(VariableDeclarationOptions? options = null)
    {
        options ??= new VariableDeclarationOptions();

        var sb = new StringBuilder();
        var first = true;

        // Get variable metadata from the source module
        var variableLookup = BuildVariableLookup();

        foreach (var argName in _call.Arguments.Keys)
        {
            if (options.ExcludeVariables.Contains(argName))
            {
                continue;
            }

            if (!first && !options.CompactSpacing)
            {
                sb.AppendLine();
            }

            first = false;
            variableLookup.TryGetValue(argName, out var sourceVar);

            var hasBody = (options.IncludeType && sourceVar?.Type is not null)
                || (options.IncludeDescription && sourceVar?.Description is not null)
                || (options.IncludeDefault && sourceVar?.Default is not null);

            if (!hasBody)
            {
                sb.AppendLine($"variable \"{argName}\" {{}}");

                continue;
            }

            sb.AppendLine($"variable \"{argName}\" {{");

            if (options.IncludeType && sourceVar?.Type is not null)
            {
                sb.AppendLine($"  type = {sourceVar.Type.ToHcl()}");
            }

            if (options.IncludeDescription && sourceVar?.Description is not null)
            {
                sb.AppendLine($"  description = \"{sourceVar.Description}\"");
            }

            if (options.IncludeDefault && sourceVar?.Default is not null)
            {
                var defaultHcl = EmitExpression(sourceVar.Default);
                sb.AppendLine($"  default = {defaultHcl}");
            }

            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits a <c>.tfvars</c> file with concrete values for the variables.
    /// When a source module is available, variable descriptions are included as comments.
    /// Inline comments from <see cref="InputValue.Comment"/> are emitted after the value.
    /// </summary>
    /// <param name="values">The variable values to emit.</param>
    /// <param name="includeQualifierComments">
    /// When <c>true</c> (default), each variable gets a <c># (Required)</c> or <c># (Optional)</c>
    /// description comment above it. Set to <c>false</c> to suppress these comments.
    /// </param>
    /// <param name="compactSpacing">
    /// When <c>true</c>, suppresses blank lines between variable assignments. Default: <c>false</c>.
    /// </param>
    public string EmitInputValues(
        IDictionary<string, InputValue> values,
        bool includeQualifierComments = true,
        bool compactSpacing = false)
    {
        var sb = new StringBuilder();
        var variableLookup = BuildVariableLookup();

        // When compact, all values form one group → align globally.
        // When not compact, blank lines separate each value into its own group → no padding needed.
        var padding = compactSpacing
            ? Utf8HclWriter.AlignAttributes(values.Keys.ToList())
            : new Dictionary<string, int>();
        var first = true;

        foreach (var kvp in values)
        {
            if (!first && !compactSpacing)
            {
                sb.AppendLine();
            }

            first = false;

            // Description comment from source module
            if (includeQualifierComments
                && variableLookup.TryGetValue(kvp.Key, out var sourceVar)
                && sourceVar.Description is not null)
            {
                var description = sourceVar.Description;
                var isRequired = IsVariableRequired(sourceVar);

                // When description already starts with (Required) or (Optional), use it as-is
                if (description.StartsWith("(Required)", StringComparison.OrdinalIgnoreCase)
                    || description.StartsWith("(Optional)", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"# {description}");
                }
                else
                {
                    var qualifier = isRequired ? "Required" : "Optional";
                    sb.AppendLine($"# ({qualifier}) {description}");
                }
            }

            var namePad = padding.TryGetValue(kvp.Key, out var p)
                ? new string(' ', p)
                : "";

            var line = $"{kvp.Key}{namePad} = {kvp.Value.Expression}";

            if (kvp.Value.Comment is not null)
            {
                line += $" # {kvp.Value.Comment}";
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes all generated files to a directory.
    /// File names and which files to generate are controlled by options.
    /// </summary>
    /// <param name="directoryPath">The directory to write to (created if needed).</param>
    /// <param name="options">Controls which files to generate and their names.</param>
    public void WriteTo(string directoryPath, FileEmitterOptions options)
    {
        Directory.CreateDirectory(directoryPath);

        var moduleFileName = options.ModuleFileName ?? $"resources-{_call.Name}.tf";
        var moduleContent = EmitModuleBlock();
        File.WriteAllText(Path.Combine(directoryPath, moduleFileName), moduleContent + "\n");

        if (options.VariablesFileName is not null)
        {
            var varContent = EmitVariableDeclarations();
            File.WriteAllText(Path.Combine(directoryPath, options.VariablesFileName), varContent);
        }

        if (options.InputFiles is not null)
        {
            foreach (var inputFile in options.InputFiles)
            {
                var inputContent = EmitInputValues(inputFile.Value);
                File.WriteAllText(Path.Combine(directoryPath, inputFile.Key), inputContent);
            }
        }
    }

    private static void EmitAttribute(
        StringBuilder sb,
        string name,
        string value,
        Dictionary<string, int> padding)
    {
        var namePad = padding.TryGetValue(name, out var p)
            ? new string(' ', p)
            : "";

        sb.AppendLine($"  {name}{namePad} = {value}");
    }

    private Dictionary<string, TerraformVariable> BuildVariableLookup()
    {
        var lookup = new Dictionary<string, TerraformVariable>();

        if (_call.SourceModule is null)
        {
            return lookup;
        }

        foreach (var v in _call.SourceModule.Variables)
        {
            lookup[v.Name] = v;
        }

        return lookup;
    }

    /// <summary>
    /// Determines whether a variable is required using the source module's classification mode.
    /// Falls back to the standard <see cref="TerraformVariable.IsRequired"/> when no module is available.
    /// </summary>
    private bool IsVariableRequired(TerraformVariable variable)
    {
        if (_call.SourceModule is null)
        {
            return variable.IsRequired;
        }

        return _call.SourceModule.RequiredVariables.Any(v => v.Name == variable.Name);
    }

    /// <summary>
    /// Emits an HCL expression back to string form using the HCL writer.
    /// </summary>
    private static string EmitExpression(HclExpression expression)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8HclWriter(stream);
        var emitter = new HclFileEmitter(writer, preserveComments: false);

        // Create a temporary file with a single attribute to emit the expression
        var tempFile = new HclFile();
        tempFile.Body.Attributes.Add(new HclAttribute
        {
            Name = "__placeholder__",
            Value = expression,
        });

        emitter.Emit(tempFile);
        writer.Flush();

        var text = Encoding.UTF8.GetString(stream.ToArray());

        // Extract just the expression part: "__placeholder__ = <expression>\n"
        var eqIndex = text.IndexOf('=');

        if (eqIndex >= 0)
        {
            return text[(eqIndex + 2)..].TrimEnd();
        }

        return text.Trim();
    }
}
