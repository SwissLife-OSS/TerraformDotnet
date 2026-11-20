using System.Text;
using Squadron;

namespace TerraformDotnet.Hcl.Tests.Common;

/// <summary>
/// Runs the real <c>terraform fmt</c> in a <c>hashicorp/terraform</c> container as an oracle for
/// emitter and formatter output. Docker must be available.
/// </summary>
public sealed class TerraformFmtResource : GenericContainerResource<TerraformFmtOptions>
{
    /// <summary>Formats HCL with <c>terraform fmt -</c> and returns the result.</summary>
    public async Task<string> FormatAsync(string hcl)
    {
        string source = Path.GetTempFileName();
        string target = $"/tmp/{Guid.NewGuid():N}.tf";

        try
        {
            await File.WriteAllTextAsync(source, hcl, new UTF8Encoding(false));
            await Manager.CopyToContainerAsync(new CopyContext(source, target));

            return await Manager.InvokeCommandAsync(["sh", "-c", $"terraform fmt - < {target} && rm {target}"])
                ?? throw new InvalidOperationException("The terraform container is not running.");
        }
        finally
        {
            File.Delete(source);
        }
    }
}

/// <summary>
/// Starts <c>hashicorp/terraform</c> with an entrypoint that keeps it running, so that
/// <c>terraform fmt</c> can be executed in it for every check.
/// </summary>
public sealed class TerraformFmtOptions : GenericContainerOptions
{
    public override void Configure(ContainerResourceBuilder builder)
    {
        base.Configure(builder);

        // The image's entrypoint is the terraform binary, which exits at once.
        builder
            .Name("terraform-fmt")
            .Image("hashicorp/terraform:1.9.8")
            .InternalPort(8080)
            .Entrypoint("tail", "-f", "/dev/null");

        ConfigureStatusChecker((_, _) => Task.FromResult(new Status { IsReady = true }));
    }
}

/// <summary>
/// Shares one <see cref="TerraformFmtResource"/> container between all <c>terraform fmt</c> checks.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TerraformFmtCollection : ICollectionFixture<TerraformFmtResource>
{
    public const string Name = "terraform fmt";
}
