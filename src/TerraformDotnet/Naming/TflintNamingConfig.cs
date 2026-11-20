using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Naming;

/// <summary>
/// Reads the rule <c>terraform_naming_convention</c> from a tflint configuration the way
/// tflint-ruleset-terraform resolves it.
/// </summary>
internal static class TflintNamingConfig
{
    private const string RuleName = "terraform_naming_convention";
    private const string DefaultFormat = "snake_case";
    private const string NoFormat = "none";

    public static TerraformNamingConvention? Read(HclFile config, TerraformNamingConvention fallback)
    {
        HclBlock? rule = FindBlock(config.Body, "rule", RuleName);

        if (!IsEnabled(config, rule))
        {
            return null;
        }

        HclBody body = rule?.Body ?? new HclBody();
        Dictionary<string, CustomFormat> customFormats = ReadCustomFormats(body);
        string format = ReadString(body, "format") ?? DefaultFormat;
        string custom = ReadString(body, "custom") ?? string.Empty;

        // A block type without its own block takes the format of the rule, not the fallback.
        NameFormat Resolve(TerraformNameKind kind, string blockType, string displayName)
        {
            NameStyle style = fallback.GetFormat(kind).Style;

            return FindBlock(body, blockType) is { } block
                ? Create(
                    ReadString(block.Body, "custom") ?? string.Empty,
                    ReadString(block.Body, "format") ?? string.Empty,
                    customFormats,
                    style,
                    displayName)
                : Create(custom, format, customFormats, style, "default");
        }

        return new TerraformNamingConvention
        {
            Resource = Resolve(TerraformNameKind.Resource, "resource", "resource"),
            Data = Resolve(TerraformNameKind.Data, "data", "data"),
            Module = Resolve(TerraformNameKind.Module, "module", "module"),
            Variable = Resolve(TerraformNameKind.Variable, "variable", "variable"),
            Output = Resolve(TerraformNameKind.Output, "output", "output"),
            Local = Resolve(TerraformNameKind.Local, "locals", "local value"),
            Check = Resolve(TerraformNameKind.Check, "check", "check"),
        };
    }

    // Without a rule block the rule runs only in the "all" preset of the terraform plugin; the
    // default "recommended" preset does not contain it.
    private static bool IsEnabled(HclFile config, HclBlock? rule)
    {
        if (rule is not null)
        {
            return ReadBool(rule.Body, "enabled") ?? true;
        }

        bool disabledByDefault = FindBlock(config.Body, "config") is { } settings
            && ReadBool(settings.Body, "disabled_by_default") == true;

        return !disabledByDefault
            && FindBlock(config.Body, "plugin", "terraform") is { } plugin
            && ReadString(plugin.Body, "preset") == "all";
    }

    private static NameFormat Create(
        string custom,
        string format,
        Dictionary<string, CustomFormat> customFormats,
        NameStyle style,
        string displayName)
    {
        try
        {
            if (custom.Length > 0)
            {
                return NameFormat.CustomPreferring(custom, style, description: null);
            }

            if (format == NoFormat)
            {
                return NameFormat.Unchecked(style);
            }

            if (customFormats.TryGetValue(format, out CustomFormat? customFormat))
            {
                return NameFormat.CustomPreferring(
                    customFormat.Regex,
                    style,
                    customFormat.Description.Length > 0 ? customFormat.Description : format);
            }

            return format.ToLowerInvariant() switch
            {
                "snake_case" => NameFormat.SnakeCase,
                "mixed_snake_case" => NameFormat.MixedSnakeCase,
                _ => throw new FormatException($"`{format}` is unsupported format"),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new FormatException($"Invalid {displayName} configuration: {ex.Message}", ex);
        }
    }

    private static Dictionary<string, CustomFormat> ReadCustomFormats(HclBody rule)
    {
        var formats = new Dictionary<string, CustomFormat>(StringComparer.Ordinal);
        HclAttribute? attribute = FindAttribute(rule, "custom_formats");

        if (attribute is null)
        {
            return formats;
        }

        if (attribute.Value is not HclObjectExpression map)
        {
            throw new FormatException("'custom_formats' must be a map of objects.");
        }

        foreach (HclObjectElement element in map.Elements)
        {
            string name = KeyOf(element);

            if (element.Value is not HclObjectExpression definition)
            {
                throw new FormatException($"Custom format '{name}' must be an object with 'regex' and 'description'.");
            }

            string regex = string.Empty;
            string description = string.Empty;

            foreach (HclObjectElement field in definition.Elements)
            {
                switch (KeyOf(field))
                {
                    case "regex":
                        regex = StringOf(field.Value, $"custom_formats.{name}.regex");
                        break;

                    case "description":
                        description = StringOf(field.Value, $"custom_formats.{name}.description");
                        break;
                }
            }

            formats[name] = new CustomFormat(regex, description);
        }

        return formats;
    }

    private static string KeyOf(HclObjectElement element) =>
        element.Key switch
        {
            HclVariableExpression identifier => identifier.Name,
            HclLiteralExpression { Kind: HclLiteralKind.String, Value: { } text } => text,
            _ => throw new FormatException("'custom_formats' keys must be names."),
        };

    private static HclBlock? FindBlock(HclBody body, string type, string? label = null)
    {
        foreach (HclBlock block in body.Blocks)
        {
            if (block.Type == type
                && (label is null ? block.Labels.Count == 0 : block.Labels.Count == 1 && block.Labels[0] == label))
            {
                return block;
            }
        }

        return null;
    }

    private static HclAttribute? FindAttribute(HclBody body, string name)
    {
        foreach (HclAttribute attribute in body.Attributes)
        {
            if (attribute.Name == name)
            {
                return attribute;
            }
        }

        return null;
    }

    private static string? ReadString(HclBody body, string name) =>
        FindAttribute(body, name) is { } attribute ? StringOf(attribute.Value, name) : null;

    private static string StringOf(HclExpression expression, string name) =>
        expression is HclLiteralExpression { Kind: HclLiteralKind.String } literal
            ? literal.Value ?? string.Empty
            : throw new FormatException($"'{name}' must be a string.");

    private static bool? ReadBool(HclBody body, string name) =>
        FindAttribute(body, name) switch
        {
            null => null,
            { Value: HclLiteralExpression { Kind: HclLiteralKind.Bool } literal } => literal.Value == "true",
            _ => throw new FormatException($"'{name}' must be true or false."),
        };

    private sealed record CustomFormat(string Regex, string Description);
}
