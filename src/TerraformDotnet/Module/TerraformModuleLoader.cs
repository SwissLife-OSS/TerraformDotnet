using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Types;

namespace TerraformDotnet.Module;

/// <summary>
/// Internal extraction logic that transforms HCL AST nodes into strongly-typed module components.
/// </summary>
internal static class TerraformModuleLoader
{
    /// <summary>Extracts all <c>variable</c> blocks from an HCL file.</summary>
    public static List<TerraformVariable> ExtractVariables(HclFile file)
    {
        var variables = new List<TerraformVariable>();

        foreach (var block in file.Body.Blocks)
        {
            if (block.Type == "variable" && block.Labels.Count == 1)
            {
                variables.Add(ParseVariable(block));
            }
        }

        return variables;
    }

    /// <summary>Extracts all <c>output</c> blocks from an HCL file.</summary>
    public static List<TerraformOutput> ExtractOutputs(HclFile file)
    {
        var outputs = new List<TerraformOutput>();

        foreach (var block in file.Body.Blocks)
        {
            if (block.Type == "output" && block.Labels.Count == 1)
            {
                outputs.Add(ParseOutput(block));
            }
        }

        return outputs;
    }

    /// <summary>Extracts all <c>resource</c> blocks from an HCL file.</summary>
    public static List<TerraformResource> ExtractResources(HclFile file)
    {
        var resources = new List<TerraformResource>();

        foreach (var block in file.Body.Blocks)
        {
            if (block.Type == "resource" && block.Labels.Count == 2)
            {
                resources.Add(ParseResource(block));
            }
        }

        return resources;
    }

    /// <summary>Extracts all <c>data</c> blocks from an HCL file.</summary>
    public static List<TerraformDataSource> ExtractDataSources(HclFile file)
    {
        var dataSources = new List<TerraformDataSource>();

        foreach (var block in file.Body.Blocks)
        {
            if (block.Type == "data" && block.Labels.Count == 2)
            {
                dataSources.Add(ParseDataSource(block));
            }
        }

        return dataSources;
    }

    /// <summary>Extracts all <c>locals</c> block attributes from an HCL file.</summary>
    public static List<TerraformLocal> ExtractLocals(HclFile file)
    {
        var locals = new List<TerraformLocal>();

        foreach (var block in file.Body.Blocks)
        {
            if (block.Type == "locals" && block.Labels.Count == 0)
            {
                foreach (var attr in block.Body.Attributes)
                {
                    locals.Add(new TerraformLocal(attr.Name, attr.Value));
                }
            }
        }

        return locals;
    }

    /// <summary>Extracts provider requirements from the <c>terraform</c> block.</summary>
    public static List<TerraformProviderRequirement> ExtractProviderRequirements(HclFile file)
    {
        var requirements = new List<TerraformProviderRequirement>();

        foreach (var block in file.Body.Blocks)
        {
            if (block.Type != "terraform" || block.Labels.Count != 0)
            {
                continue;
            }

            foreach (var subBlock in block.Body.Blocks)
            {
                if (subBlock.Type != "required_providers" || subBlock.Labels.Count != 0)
                {
                    continue;
                }

                foreach (var attr in subBlock.Body.Attributes)
                {
                    requirements.Add(ParseProviderRequirement(attr));
                }
            }
        }

        return requirements;
    }

    /// <summary>Extracts <c>required_version</c> from the <c>terraform</c> block.</summary>
    public static string? ExtractRequiredVersion(HclFile file)
    {
        foreach (var block in file.Body.Blocks)
        {
            if (block.Type != "terraform" || block.Labels.Count != 0)
            {
                continue;
            }

            var versionAttr = FindAttribute(block.Body, "required_version");

            if (versionAttr?.Value is HclLiteralExpression { Kind: HclLiteralKind.String } lit)
            {
                return lit.Value;
            }
        }

        return null;
    }

    private static TerraformVariable ParseVariable(HclBlock block)
    {
        var name = block.Labels[0];
        TerraformType? type = null;
        string? description = null;
        HclExpression? defaultValue = null;
        var isSensitive = false;
        var isNullable = false;
        TerraformValidation? validation = null;

        var typeAttr = FindAttribute(block.Body, "type");

        if (typeAttr is not null)
        {
            type = TerraformType.Parse(typeAttr.Value);
        }

        var descAttr = FindAttribute(block.Body, "description");

        if (descAttr?.Value is HclLiteralExpression { Kind: HclLiteralKind.String } descLit)
        {
            description = descLit.Value;
        }

        var defaultAttr = FindAttribute(block.Body, "default");

        if (defaultAttr is not null)
        {
            defaultValue = defaultAttr.Value;
        }

        var sensitiveAttr = FindAttribute(block.Body, "sensitive");

        if (sensitiveAttr?.Value is HclLiteralExpression { Kind: HclLiteralKind.Bool } senLit)
        {
            isSensitive = senLit.Value == "true";
        }

        var nullableAttr = FindAttribute(block.Body, "nullable");

        if (nullableAttr?.Value is HclLiteralExpression { Kind: HclLiteralKind.Bool } nullLit)
        {
            isNullable = nullLit.Value == "true";
        }

        // Parse validation sub-block
        foreach (var subBlock in block.Body.Blocks)
        {
            if (subBlock.Type == "validation" && subBlock.Labels.Count == 0)
            {
                validation = ParseValidation(subBlock);

                break; // Only first validation block is used
            }
        }

        return new TerraformVariable(name, type, description, defaultValue, isSensitive, isNullable, validation);
    }

    private static TerraformValidation ParseValidation(HclBlock block)
    {
        var conditionAttr = FindAttribute(block.Body, "condition");
        var errorAttr = FindAttribute(block.Body, "error_message");

        var condition = conditionAttr?.Value
            ?? throw new FormatException("Validation block is missing 'condition' attribute.");

        var errorMessage = errorAttr?.Value is HclLiteralExpression { Kind: HclLiteralKind.String } lit
            ? lit.Value ?? string.Empty
            : throw new FormatException("Validation block is missing 'error_message' attribute.");

        return new TerraformValidation(condition, errorMessage);
    }

    private static TerraformOutput ParseOutput(HclBlock block)
    {
        var name = block.Labels[0];

        var valueAttr = FindAttribute(block.Body, "value")
            ?? throw new FormatException($"Output '{name}' is missing required 'value' attribute.");

        string? description = null;
        var descAttr = FindAttribute(block.Body, "description");

        if (descAttr?.Value is HclLiteralExpression { Kind: HclLiteralKind.String } descLit)
        {
            description = descLit.Value;
        }

        var isSensitive = false;
        var sensitiveAttr = FindAttribute(block.Body, "sensitive");

        if (sensitiveAttr?.Value is HclLiteralExpression { Kind: HclLiteralKind.Bool } senLit)
        {
            isSensitive = senLit.Value == "true";
        }

        var dependsOn = ExtractDependsOn(block.Body);

        return new TerraformOutput(name, valueAttr.Value, description, isSensitive, dependsOn);
    }

    private static TerraformResource ParseResource(HclBlock block)
    {
        var resourceType = block.Labels[0];
        var resourceName = block.Labels[1];

        var count = FindAttribute(block.Body, "count")?.Value;
        var forEach = FindAttribute(block.Body, "for_each")?.Value;
        var dependsOn = ExtractDependsOn(block.Body);

        string? provider = null;
        var providerAttr = FindAttribute(block.Body, "provider");

        if (providerAttr is not null)
        {
            provider = ExtractExpressionText(providerAttr.Value);
        }

        return new TerraformResource(resourceType, resourceName, block.Body,
            count, forEach, dependsOn, provider);
    }

    private static TerraformDataSource ParseDataSource(HclBlock block)
    {
        var dataType = block.Labels[0];
        var dataName = block.Labels[1];

        var count = FindAttribute(block.Body, "count")?.Value;
        var forEach = FindAttribute(block.Body, "for_each")?.Value;

        return new TerraformDataSource(dataType, dataName, block.Body, count, forEach);
    }

    private static TerraformProviderRequirement ParseProviderRequirement(HclAttribute attr)
    {
        var name = attr.Name;
        string? source = null;
        string? version = null;

        if (attr.Value is HclObjectExpression obj)
        {
            foreach (var element in obj.Elements)
            {
                var key = ExtractFieldName(element.Key);

                if (key == "source" && element.Value is HclLiteralExpression { Kind: HclLiteralKind.String } srcLit)
                {
                    source = srcLit.Value;
                }
                else if (key == "version" && element.Value is HclLiteralExpression { Kind: HclLiteralKind.String } verLit)
                {
                    version = verLit.Value;
                }
            }
        }

        return new TerraformProviderRequirement(name, source, version);
    }

    private static HclAttribute? FindAttribute(HclBody body, string name)
    {
        foreach (var attr in body.Attributes)
        {
            if (attr.Name == name)
            {
                return attr;
            }
        }

        return null;
    }

    private static IReadOnlyList<string>? ExtractDependsOn(HclBody body)
    {
        var attr = FindAttribute(body, "depends_on");

        if (attr?.Value is not HclTupleExpression tuple)
        {
            return null;
        }

        var deps = new List<string>(tuple.Elements.Count);

        foreach (var element in tuple.Elements)
        {
            var text = ExtractExpressionText(element);

            if (text is not null)
            {
                deps.Add(text);
            }
        }

        return deps.Count > 0 ? deps : null;
    }

    /// <summary>
    /// Extracts textual representation from simple expressions (variable references, member access).
    /// </summary>
    private static string? ExtractExpressionText(HclExpression expression) => expression switch
    {
        HclVariableExpression v => v.Name,
        HclAttributeAccessExpression m => ExtractAttributeAccessText(m),
        HclLiteralExpression { Kind: HclLiteralKind.String } lit => lit.Value,
        _ => null,
    };

    private static string ExtractAttributeAccessText(HclAttributeAccessExpression expr)
    {
        var source = ExtractExpressionText(expr.Source) ?? expr.Source.ToString()!;

        return $"{source}.{expr.Name}";
    }

    private static string ExtractFieldName(HclExpression key) => key switch
    {
        HclVariableExpression v => v.Name,
        HclLiteralExpression { Kind: HclLiteralKind.String } lit => lit.Value ?? string.Empty,
        _ => key.ToString() ?? string.Empty,
    };
}
