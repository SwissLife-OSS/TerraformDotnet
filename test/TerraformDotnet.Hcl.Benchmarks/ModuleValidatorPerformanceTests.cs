using System.Diagnostics;
using System.Text;
using TerraformDotnet.Evaluation;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;
using TerraformDotnet.Validation;
using Xunit.Abstractions;

namespace TerraformDotnet.Hcl.Benchmarks;

/// <summary>
/// Baseline performance tests for the expression evaluator and <see cref="ModuleValidator"/>.
/// Lukla validates on every debounced UI edit, so the cost of one <c>Validate</c> call on a realistic
/// module (about 120 variables and 150 validations) is the number that matters. Each test prints
/// time and allocations per call; the thresholds are deliberately generous and only catch regressions.
/// </summary>
public class ModuleValidatorPerformanceTests(ITestOutputHelper output)
{
    private const int Groups = 30;
    private const int MeasureIterations = 2_000;

    // Tiered compilation promotes hot methods after a delay, so the warm-up is time-based.
    private static readonly TimeSpan WarmupDuration = TimeSpan.FromMilliseconds(400);

    // Threshold values per call. Time is generous so that it does not flake on a busy CI machine.
    // Allocations are stable, and the limit is well below the ~1 MB per call the validator needed
    // when every validation copied all variables, so it catches that regression.
    private const double ValidateMaxMs = 2;
    private const double ValidateMaxKilobytes = 400;
    private const double EvaluateMaxMs = 1;

    private static readonly ModuleValidator Validator = new(BuildModule());

    /// <summary>
    /// Builds a module of <see cref="Groups"/> groups of four variables: an enum with a locals-driven
    /// validation, a bounded number, a regex-checked name and a nullable variable that becomes
    /// required when the enum is "gold".
    /// </summary>
    private static TerraformModule BuildModule()
    {
        var hcl = new StringBuilder();
        for (var i = 0; i < Groups; i++)
        {
            hcl.AppendLine($$"""
                variable "sla_{{i}}" {
                  type    = string
                  default = "silver"
                  validation {
                    condition     = contains(["silver", "gold"], var.sla_{{i}})
                    error_message = "sla_{{i}} must be silver or gold."
                  }
                  validation {
                    condition     = local.tier_{{i}} > 0
                    error_message = "tier_{{i}} must be positive."
                  }
                }

                variable "replicas_{{i}}" {
                  type    = number
                  default = 3
                  validation {
                    condition     = var.replicas_{{i}} >= 1 && var.replicas_{{i}} <= 10
                    error_message = "replicas_{{i}} must be between 1 and 10."
                  }
                }

                variable "name_{{i}}" {
                  type    = string
                  default = "app-{{i}}"
                  validation {
                    condition     = can(regex("^[a-z][a-z0-9-]{2,30}$", var.name_{{i}}))
                    error_message = "name_{{i}} must be a DNS label."
                  }
                }

                variable "backup_{{i}}" {
                  type    = string
                  default = null
                  validation {
                    condition     = var.sla_{{i}} != "gold" || var.backup_{{i}} != null
                    error_message = "backup_{{i}} is required for gold."
                  }
                }

                locals {
                  tier_{{i}} = var.sla_{{i}} == "gold" ? 2 : 1
                }
                """);
        }

        return TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl.ToString()));
    }

    private static Dictionary<string, HclValue> DefaultsOnly() => [];

    private static Dictionary<string, HclValue> AllSupplied(bool gold)
    {
        var values = new Dictionary<string, HclValue>(StringComparer.Ordinal);
        for (var i = 0; i < Groups; i++)
        {
            values[$"sla_{i}"] = HclValue.FromString(gold ? "gold" : "silver");
            values[$"replicas_{i}"] = HclValue.FromNumber(5);
            values[$"name_{i}"] = HclValue.FromString($"service-{i}");
            values[$"backup_{i}"] = gold ? HclValue.FromString($"vault-{i}") : HclValue.Null;
        }

        return values;
    }

    private (double Milliseconds, double Kilobytes) Measure(string label, Action action)
    {
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed < WarmupDuration)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < MeasureIterations; i++)
        {
            action();
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var milliseconds = stopwatch.Elapsed.TotalMilliseconds / MeasureIterations;
        var kilobytes = allocated / 1024.0 / MeasureIterations;
        output.WriteLine($"{label}: {milliseconds * 1000:F1} µs/call, {kilobytes:F1} KB/call");

        return (milliseconds, kilobytes);
    }

    [Fact]
    public void Validate_DefaultsOnly()
    {
        var values = DefaultsOnly();

        var (milliseconds, kilobytes) = Measure("Validate (defaults only, 120 variables)", () => Validator.Validate(values));

        Assert.True(milliseconds < ValidateMaxMs, $"{milliseconds:F3} ms/call exceeds {ValidateMaxMs} ms");
        Assert.True(kilobytes < ValidateMaxKilobytes, $"{kilobytes:F1} KB/call exceeds {ValidateMaxKilobytes} KB");
    }

    [Fact]
    public void Validate_AllSupplied()
    {
        var values = AllSupplied(gold: false);

        var (milliseconds, kilobytes) = Measure("Validate (all supplied)", () => Validator.Validate(values));

        Assert.True(milliseconds < ValidateMaxMs, $"{milliseconds:F3} ms/call exceeds {ValidateMaxMs} ms");
        Assert.True(kilobytes < ValidateMaxKilobytes, $"{kilobytes:F1} KB/call exceeds {ValidateMaxKilobytes} KB");
    }

    [Fact]
    public void Validate_GoldWithRequiredNowCandidates()
    {
        var values = AllSupplied(gold: true);
        values["backup_0"] = HclValue.Null;

        var (milliseconds, kilobytes) = Measure("Validate (gold, null backups)", () => Validator.Validate(values));

        Assert.True(milliseconds < ValidateMaxMs, $"{milliseconds:F3} ms/call exceeds {ValidateMaxMs} ms");
        Assert.True(kilobytes < ValidateMaxKilobytes, $"{kilobytes:F1} KB/call exceeds {ValidateMaxKilobytes} KB");
    }

    [Fact]
    public void Validate_Expressions()
    {
        var values = new Dictionary<string, HclExpression>(StringComparer.Ordinal);
        for (var i = 0; i < Groups; i++)
        {
            values[$"sla_{i}"] = HclExpression.Parse("\"gold\"");
            values[$"replicas_{i}"] = HclExpression.Parse("2 + 3");
            values[$"name_{i}"] = HclExpression.Parse($"\"service-{i}\"");
        }

        var (milliseconds, kilobytes) = Measure("Validate (HCL expressions)", () => Validator.Validate(values));

        Assert.True(milliseconds < ValidateMaxMs, $"{milliseconds:F3} ms/call exceeds {ValidateMaxMs} ms");
        Assert.True(kilobytes < ValidateMaxKilobytes, $"{kilobytes:F1} KB/call exceeds {ValidateMaxKilobytes} KB");
    }

    [Fact]
    public void Evaluate_EnumCondition()
    {
        var evaluator = new HclEvaluator(new HclEvaluatorOptions { FunctionResolver = TerraformFunctions.Default });
        var expression = HclExpression.Parse("contains([\"silver\", \"gold\", \"bronze\"], var.sla) && var.replicas >= 1 && var.replicas <= 10");
        var variables = new Dictionary<string, HclValue>
        {
            ["sla"] = HclValue.FromString("gold"),
            ["replicas"] = HclValue.FromNumber(5),
        };
        var context = new HclEvaluationContext();
        context.SetVariable("var", HclValue.FromObject(variables));

        var (milliseconds, _) = Measure("Evaluate enum + range condition", () => evaluator.Evaluate(expression, context));

        Assert.True(milliseconds < EvaluateMaxMs, $"{milliseconds:F3} ms/call exceeds {EvaluateMaxMs} ms");
    }
}
