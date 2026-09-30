using System.Text.Json;
using TerraformDotnet.Evaluation;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Tests.Evaluation;

/// <summary>
/// Compares the function library with the recorded output of a real <c>terraform console</c>
/// (see <c>__assets__/golden/generate.py</c>).
/// </summary>
public class TerraformFunctionGoldenTests
{
    private static readonly HclEvaluator Evaluator = new(new HclEvaluatorOptions
    {
        FunctionResolver = TerraformFunctions.Default,
    });

    private static readonly Lazy<Dictionary<string, GoldenCase>> Golden = new(Load);

    /// <summary>
    /// Expressions the library intentionally evaluates as unknown because it cannot reproduce
    /// Terraform's behavior exactly; an unknown result never fails a validation.
    /// </summary>
    private static readonly HashSet<string> IntentionallyUnknown =
    [
        "format(\"%e\", 1234.5)",
        "format(\"%g\", 1234.5)",
        "format(\"%\")",
        "format(\"%z\", 1)",
        "format(\"%c\", 65)",
        "format(\"%U\", 65)",
        "format(\"%x\", 3.5)",
        "cidrhost(\"010.0.0.0/8\", 1)",
        "cidrhost(\"::ffff:10.0.0.0/104\", 1)",
        "cidrsubnet(\"10.0.0.0/16\", -1, 0)",
        "cidrsubnet(\"10.0.0.0/16\", 8, -1)",
        "cidrsubnet(\"10.0.0.0/16\", 8, -2)",
        "regex(\"[[:^alpha:]]\", \"1\")",
        "regex(\"(?m)^b\", \"a\\nb\")",
        "replace(\"hello\", \"/(l+)/\", \"[$1]\")",
    ];

    /// <summary>Documented deviations from Terraform that are not merely "unknown".</summary>
    private static readonly Dictionary<string, string> KnownDeviations = new()
    {
        ["0.1+0.2"] = "numbers are IEEE doubles, Terraform uses arbitrary precision",
        ["0.1+0.2 == 0.3"] = "numbers are IEEE doubles, Terraform uses arbitrary precision",
        ["sum([0.1,0.2])"] = "numbers are IEEE doubles, Terraform uses arbitrary precision",
        ["jsonencode(12345678901234567890)"] = "numbers are IEEE doubles, Terraform uses arbitrary precision",
        ["tostring(12345678901234567890)"] = "numbers are IEEE doubles, Terraform uses arbitrary precision",
        ["parseint(\"9999999999999999999999\", 10)"] = "numbers are IEEE doubles, Terraform uses arbitrary precision",
        ["tostring(-0.0)"] = "negative zero prints as 0",
        ["tonumber(\"-0\")"] = "negative zero prints as 0",
        ["jsondecode(\"123456789012345678901234567890\")"] = "numbers are IEEE doubles, Terraform uses arbitrary precision",
    };

    public static TheoryData<string> Expressions()
    {
        var data = new TheoryData<string>();
        foreach (var expression in Golden.Value.Keys)
        {
            data.Add(expression);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Expressions))]
    public void MatchesTerraform(string expression)
    {
        if (KnownDeviations.ContainsKey(expression))
        {
            return;
        }

        var golden = Golden.Value[expression];
        var wrapped = HclExpression.Parse($"jsonencode({expression})");

        HclValue actual;
        try
        {
            actual = Evaluator.Evaluate(wrapped, new HclEvaluationContext());
        }
        catch (Exception exception) when (!golden.Ok && IsRecoverable(exception))
        {
            return;
        }

        if (actual.Type == HclValueType.Unknown)
        {
            Assert.True(IntentionallyUnknown.Contains(expression), $"'{expression}' evaluated to unknown but is not listed as intentionally unsupported.");

            return;
        }

        Assert.True(golden.Ok, $"'{expression}' should fail in Terraform ({golden.Error}) but evaluated to {actual}.");
        Assert.Equal(golden.Json, actual.StringValue);
        Assert.False(IntentionallyUnknown.Contains(expression), $"'{expression}' is listed as intentionally unknown but evaluates fine; remove it from the list.");
    }

    [Fact]
    public void GoldenFileIsRecordedFromTerraform()
    {
        Assert.NotEmpty(Golden.Value);
        Assert.All(KnownDeviations.Keys, expression => Assert.Contains(expression, Golden.Value.Keys));
        Assert.All(IntentionallyUnknown, expression => Assert.Contains(expression, Golden.Value.Keys));
    }

    private static bool IsRecoverable(Exception exception)
        => exception is InvalidOperationException or ArgumentException or FormatException or OverflowException or KeyNotFoundException or InvalidCastException;

    private static Dictionary<string, GoldenCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "__assets__", "golden", "functions.golden.jsonl");
        var cases = new Dictionary<string, GoldenCase>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var ok = root.GetProperty("ok").GetBoolean();
            cases[root.GetProperty("expr").GetString()!] = new GoldenCase(
                ok,
                ok ? root.GetProperty("json").GetString() : null,
                ok ? null : root.GetProperty("error").GetString());
        }

        return cases;
    }

    private sealed record GoldenCase(bool Ok, string? Json, string? Error);
}
