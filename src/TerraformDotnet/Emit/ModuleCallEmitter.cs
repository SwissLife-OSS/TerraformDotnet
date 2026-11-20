using System.Text;
using TerraformDotnet.Hcl.Exceptions;
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
    /// Emits the <c>module "name" { ... }</c> block as an HCL string, formatted like
    /// <c>terraform fmt</c>.
    /// </summary>
    public string EmitModuleBlock()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"module \"{_call.Name}\" {{");

        AppendAttribute(sb, "source", $"\"{_call.Source}\"");

        if (_call.Version is not null)
        {
            AppendAttribute(sb, "version", $"\"{_call.Version}\"");
        }

        if (_call.Arguments.Count > 0)
        {
            sb.AppendLine();

            foreach (var arg in _call.Arguments)
            {
                AppendAttribute(sb, arg.Key, arg.Value);
            }
        }

        // Commented optional variables. terraform fmt does not align text inside comments, so
        // the names are padded here.
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

        // Meta-arguments
        var metaArguments = new List<KeyValuePair<string, string>>();

        if (_call.Count is not null)
        {
            metaArguments.Add(new("count", _call.Count));
        }

        if (_call.ForEach is not null)
        {
            metaArguments.Add(new("for_each", _call.ForEach));
        }

        if (_call.DependsOn is not null)
        {
            var deps = string.Join(", ", _call.DependsOn);
            metaArguments.Add(new("depends_on", $"[{deps}]"));
        }

        if (_call.Providers is not null)
        {
            var pairs = _call.Providers.Select(p => $"{p.Key} = {p.Value}");
            metaArguments.Add(new("providers", $"{{ {string.Join(", ", pairs)} }}"));
        }

        if (metaArguments.Count > 0)
        {
            if (_call.Arguments.Count > 0 || _call.CommentedOptionalVariables.Count > 0)
            {
                sb.AppendLine();
            }

            foreach (var meta in metaArguments)
            {
                AppendAttribute(sb, meta.Key, meta.Value);
            }
        }

        sb.Append('}');

        return FormatHcl(sb.ToString());
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
                AppendAttribute(sb, "type", sourceVar.Type.ToHcl());
            }

            if (options.IncludeDescription && sourceVar?.Description is not null)
            {
                AppendAttribute(sb, "description", $"\"{sourceVar.Description}\"");
            }

            if (options.IncludeDefault && sourceVar?.Default is not null)
            {
                AppendAttribute(sb, "default", EmitExpression(sourceVar.Default));
            }

            sb.AppendLine("}");
        }

        return FormatHcl(sb.ToString());
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

            AppendInputValue(sb, kvp.Key, kvp.Value);
        }

        var formatted = FormatHcl(sb.ToString());
        sb.Clear().Append(formatted);

        // Commented sentinel variables (injected externally, e.g. by a CI/CD pipeline)
        if (_call.CommentedInputVariables.Count > 0)
        {
            if (values.Count > 0)
            {
                sb.AppendLine();
            }

            var commentedNames = _call.CommentedInputVariables
                .Select(c => c.Name)
                .ToList();
            var commentedPadding = Utf8HclWriter.AlignAttributes(commentedNames);

            foreach (var commented in _call.CommentedInputVariables)
            {
                if (commented.Description is not null)
                {
                    sb.AppendLine($"# {commented.Description}");
                }

                var namePad = commentedPadding.TryGetValue(commented.Name, out var cp)
                    ? new string(' ', cp)
                    : "";
                sb.AppendLine($"# {commented.Name}{namePad} = {commented.SuggestedExpression}");
            }
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

    private static void AppendAttribute(StringBuilder sb, string name, string value)
    {
        sb.Append("  ").Append(name).Append(" = ").AppendLine(value);
    }

    private static void AppendInputValue(StringBuilder sb, string name, InputValue input)
    {
        sb.Append(name).Append(" = ").Append(input.Expression);

        if (input.Comment is null)
        {
            sb.AppendLine();
        }
        else if (EndsWithHeredoc(input.Expression))
        {
            // A comment after the end marker would become part of the marker line.
            sb.AppendLine().Append("# ").AppendLine(input.Comment);
        }
        else
        {
            sb.Append(" # ").AppendLine(input.Comment);
        }
    }

    /// <summary>Determines whether the last line of a value is the end marker of a heredoc.</summary>
    private static bool EndsWithHeredoc(string expression)
    {
        int lastBreak = expression.LastIndexOf('\n');

        if (lastBreak < 0)
        {
            return false;
        }

        string marker = expression[(lastBreak + 1)..].Trim();

        return marker.Length > 0
            && (expression.Contains($"<<{marker}", StringComparison.Ordinal)
                || expression.Contains($"<<-{marker}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Formats generated HCL like <c>terraform fmt</c>. Text that is not valid HCL, for example
    /// because a caller supplied an invalid expression, is returned as built.
    /// </summary>
    private static string FormatHcl(string hcl)
    {
        try
        {
            return HclFormatter.Format(hcl);
        }
        catch (HclException)
        {
            return hcl;
        }
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
    /// Emits an <see cref="HclExpression"/> AST node to a canonical HCL string.
    /// Uses the same <see cref="HclFileEmitter"/> pipeline,
    /// producing correctly indented output.
    /// <example>
    /// <code>
    /// var expr = new HclFunctionCallExpression
    /// {
    ///     Name = "flatten",
    ///     Arguments = { new HclTupleExpression() }
    /// };
    /// string hcl = ModuleCallEmitter.EmitExpression(expr);
    /// // "flatten([])"
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="expression">The HCL expression AST node to emit.</param>
    /// <returns>The emitted HCL string.</returns>
    public static string EmitExpression(HclExpression expression)
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
