using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Tests.Common;
using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Tests.RoundTrip;

/// <summary>
/// Checks the formatter and the emitter against the real-world module files in the shared test assets.
/// </summary>
public sealed partial class HclAssetFormattingTests
{
    private static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "__assets__");

    public static TheoryData<string> Assets()
    {
        var data = new TheoryData<string>();

        foreach (string path in Directory.GetFiles(AssetsDirectory, "*.tf", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);

            if (!string.IsNullOrWhiteSpace(text))
            {
                data.Add(Path.GetRelativePath(AssetsDirectory, path).Replace('\\', '/'));
            }
        }

        return data;
    }

    [Fact]
    public void Assets_AreCopiedToOutput()
    {
        Assert.True(Assets().Count >= 15);
    }

    [Theory]
    [MemberData(nameof(Assets))]
    public void Format_FormattedAsset_IsUnchanged(string asset)
    {
        string formatted = HclFormatter.Format(StripLayout(Read(asset)));

        Assert.Equal(formatted, HclFormatter.Format(formatted));
    }

    [Theory]
    [MemberData(nameof(Assets))]
    public void Emit_ParsedAsset_RoundTripsThroughParser(string asset)
    {
        string emitted = Emit(Parse(Read(asset)));

        Assert.Equal(emitted, Emit(Parse(emitted)));
    }

    private static string Read(string asset) => File.ReadAllText(Path.Combine(AssetsDirectory, asset)).ReplaceLineEndings("\n");

    /// <summary>
    /// Removes the layout that <c>terraform fmt</c> owns: indentation, the padding before an
    /// assignment <c>=</c> and the padding before a trailing comment. Heredoc bodies are kept.
    /// </summary>
    private static string StripLayout(string hcl)
    {
        var output = new StringBuilder();
        string? heredocMarker = null;

        foreach (string line in hcl.Split('\n'))
        {
            if (heredocMarker is not null)
            {
                output.Append(line).Append('\n');

                if (line.Trim() == heredocMarker)
                {
                    heredocMarker = null;
                }

                continue;
            }

            string stripped = AssignmentPadding().Replace(line.TrimStart(), "$1 = ");
            stripped = CommentPadding().Replace(stripped, "$1 $2");
            output.Append(stripped).Append('\n');

            Match heredoc = HeredocStart().Match(stripped);

            if (heredoc.Success)
            {
                heredocMarker = heredoc.Groups[1].Value;
            }
        }

        return output.ToString(0, output.Length - 1);
    }

    private static string Emit(HclFile file)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8HclWriter(buffer);
        new HclFileEmitter(writer).Emit(file);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static HclFile Parse(string hcl) => HclFileParser.Parse(Encoding.UTF8.GetBytes(hcl));

    [GeneratedRegex("""^([A-Za-z0-9_\-"]+) +=(?![=>]) *""")]
    private static partial Regex AssignmentPadding();

    [GeneratedRegex("""([^\s"]) {2,}(#|//)""")]
    private static partial Regex CommentPadding();

    [GeneratedRegex("""<<-?([A-Za-z_][A-Za-z0-9_]*)\s*$""")]
    private static partial Regex HeredocStart();

    /// <summary>Checks against the real <c>terraform fmt</c> running in a container.</summary>
    [Collection(TerraformFmtCollection.Name)]
    public sealed class AgainstTerraformFmt(TerraformFmtResource terraform)
    {
        [Theory]
        [MemberData(nameof(Assets), MemberType = typeof(HclAssetFormattingTests))]
        public async Task Format_Asset_MatchesTerraformFmt(string asset)
        {
            string hcl = Read(asset);

            Assert.Equal(await terraform.FormatAsync(hcl), HclFormatter.Format(hcl));
        }

        [Theory]
        [MemberData(nameof(Assets), MemberType = typeof(HclAssetFormattingTests))]
        public async Task Format_AssetWithoutLayout_MatchesTerraformFmt(string asset)
        {
            string hcl = StripLayout(Read(asset));

            Assert.Equal(await terraform.FormatAsync(hcl), HclFormatter.Format(hcl));
        }

        [Theory]
        [MemberData(nameof(Assets), MemberType = typeof(HclAssetFormattingTests))]
        public async Task Emit_ParsedAsset_IsStableUnderTerraformFmt(string asset)
        {
            string emitted = Emit(Parse(Read(asset)));

            Assert.Equal(await terraform.FormatAsync(emitted), emitted);
        }
    }
}
