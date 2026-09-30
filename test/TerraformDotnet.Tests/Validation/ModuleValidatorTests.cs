using System.Text;
using TerraformDotnet.Evaluation;
using TerraformDotnet.Hcl.Evaluation;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;
using TerraformDotnet.Validation;

namespace TerraformDotnet.Tests.Validation;

public class ModuleValidatorTests
{
    private static readonly TerraformModule SlaModule = TerraformModule.LoadFromDirectory("__assets__/sla-module");
    private static readonly TerraformModule AvmModule = TerraformModule.LoadFromDirectory("__assets__/avm-like-module");

    private static TerraformModule Parse(string hcl) => TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    private static Dictionary<string, HclValue> Values(params (string Name, string Expression)[] values)
    {
        var evaluator = new HclEvaluator(new HclEvaluatorOptions { FunctionResolver = TerraformFunctions.Default });

        return values.ToDictionary(
            v => v.Name,
            v => evaluator.Evaluate(HclExpression.Parse(v.Expression), new HclEvaluationContext()),
            StringComparer.Ordinal);
    }

    private static ValidationReport Validate(TerraformModule module, params (string Name, string Expression)[] values)
        => new ModuleValidator(module).Validate(Values(values));

    private static ValidationOutcome Outcome(ValidationReport report, string variable, int index = 0)
        => report.For(variable).Single(r => r.Index == index).Outcome;

    [Fact]
    public void ValidValuesPass()
    {
        var report = Validate(SlaModule, ("sla", "\"silver\""));

        Assert.False(report.HasFailures);
        Assert.False(report.HasIndeterminate);
        Assert.All(report.Results, r => Assert.Equal(ValidationOutcome.Passed, r.Outcome));
        Assert.DoesNotContain(report.RequiredNow, n => n != "sla");
    }

    [Fact]
    public void EnumViolationFailsWithTheErrorMessage()
    {
        var report = Validate(SlaModule, ("sla", "\"platinum\""));

        var failure = Assert.Single(report.Failures);
        Assert.Equal("sla", failure.VariableName);
        Assert.Equal(0, failure.Index);
        Assert.Equal("sla must be silver or gold.", failure.ErrorMessage);
        Assert.Null(failure.Reason);
        Assert.Equal(["sla"], failure.ReferencedVariables);
    }

    [Fact]
    public void ReportsEveryValidationOfAVariable()
    {
        var report = Validate(SlaModule, ("sla", "\"gold\""), ("replicas", "20"));

        Assert.Equal(ValidationOutcome.Failed, Outcome(report, "replicas", 0));
        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "replicas", 1));
        Assert.Equal("replicas must be between 1 and 10.", report.For("replicas")[0].ErrorMessage);
    }

    [Fact]
    public void GoldRequiresTheBackupVaultAndSilverDoesNot()
    {
        var gold = Validate(SlaModule, ("sla", "\"gold\""), ("replicas", "3"));
        var silver = Validate(SlaModule, ("sla", "\"silver\""));

        Assert.True(gold.IsRequiredNow("backup_vault_id"));
        Assert.Contains("backup_vault_id", gold.RequiredNow);
        Assert.Equal(ValidationOutcome.Failed, Outcome(gold, "backup_vault_id"));
        Assert.Equal("backup_vault_id is required for the gold SLA.", gold.For("backup_vault_id")[0].ErrorMessage);

        Assert.False(silver.IsRequiredNow("backup_vault_id"));
        Assert.Equal(ValidationOutcome.Passed, Outcome(silver, "backup_vault_id"));
    }

    [Fact]
    public void GoldWithAVaultPasses()
    {
        var report = Validate(
            SlaModule,
            ("sla", "\"gold\""),
            ("replicas", "3"),
            ("backup_vault_id", "\"vault-1\""));

        Assert.False(report.HasFailures);
        Assert.True(report.IsRequiredNow("backup_vault_id"));
    }

    [Fact]
    public void RequiredVariablesWithoutAValueAreAlwaysRequired()
    {
        var report = Validate(SlaModule);

        Assert.Contains("sla", report.RequiredNow);
        Assert.DoesNotContain("region", report.RequiredNow);
    }

    [Fact]
    public void ValidationsThatDependOnAnUnsetRequiredVariableAreIndeterminate()
    {
        var report = Validate(SlaModule);

        Assert.False(report.HasFailures);
        var enumCheck = report.For("sla").Single();
        Assert.Equal(ValidationOutcome.Indeterminate, enumCheck.Outcome);
        Assert.Contains("var.sla", enumCheck.Reason, StringComparison.Ordinal);
        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "replicas", 1));
        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "replicas", 0));
    }

    [Fact]
    public void DefaultsAreUsedForMissingValues()
    {
        var report = Validate(SlaModule, ("sla", "\"silver\""));

        Assert.Equal(1, report.Values["replicas"].NumberValue);
        Assert.Equal("westeurope", report.Values["region"].StringValue);
        Assert.Equal(HclValue.Null, report.Values["backup_vault_id"]);
    }

    [Fact]
    public void ExplicitNullIsValidated()
    {
        var report = Validate(SlaModule, ("sla", "\"gold\""), ("backup_vault_id", "null"));

        Assert.Equal(ValidationOutcome.Failed, Outcome(report, "backup_vault_id"));
    }

    [Fact]
    public void FunctionsThatRejectNullAreIndeterminateInsteadOfFailed()
    {
        var report = Validate(SlaModule, ("sla", "null"));

        var check = report.For("sla").Single();
        Assert.Equal(ValidationOutcome.Indeterminate, check.Outcome);
        Assert.Contains("contains", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NonNullableVariablesFallBackToTheirDefault()
    {
        var module = Parse("""
            variable "size" {
              type     = number
              default  = 3
              nullable = false

              validation {
                condition     = var.size >= 1
                error_message = "size must be positive."
              }
            }
            """);

        var report = Validate(module, ("size", "null"));

        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "size"));
        Assert.Equal(3, report.Values["size"].NumberValue);
    }

    [Fact]
    public void NonNullableVariablesWithoutADefaultReportATypeError()
    {
        var module = Parse("""
            variable "size" {
              type     = number
              nullable = false

              validation {
                condition     = var.size >= 1
                error_message = "size must be positive."
              }
            }
            """);

        var report = Validate(module, ("size", "null"));

        Assert.Contains("size", report.TypeErrors.Keys);
        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "size"));
    }

    [Fact]
    public void ValuesAreConvertedBeforeTheyAreValidated()
    {
        var report = Validate(SlaModule, ("sla", "\"gold\""), ("replicas", "\"5\""), ("backup_vault_id", "\"v\""));

        Assert.False(report.HasFailures);
        Assert.Equal(5, report.Values["replicas"].NumberValue);
    }

    [Fact]
    public void ConversionFailuresAreTypeErrorsAndMakeTheVariablesValidationsIndeterminate()
    {
        var report = Validate(SlaModule, ("sla", "\"gold\""), ("replicas", "\"many\""));

        Assert.Contains("replicas", report.TypeErrors.Keys);
        Assert.All(report.For("replicas"), r => Assert.Equal(ValidationOutcome.Indeterminate, r.Outcome));
    }

    [Fact]
    public void ValidationsOfOtherVariablesStillRunWhenOneVariableHasATypeError()
    {
        var report = Validate(SlaModule, ("sla", "\"bronze\""), ("replicas", "\"many\""));

        Assert.Equal(ValidationOutcome.Failed, Outcome(report, "sla"));
        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "replicas", 1));
    }

    [Fact]
    public void ExpressionOverloadEvaluatesLiteralsAndFunctions()
    {
        var expressions = new Dictionary<string, HclExpression>
        {
            ["sla"] = HclExpression.Parse("lower(\"GOLD\")"),
            ["replicas"] = HclExpression.Parse("1 + 2"),
        };

        var report = new ModuleValidator(SlaModule).Validate(expressions);

        Assert.Equal("gold", report.Values["sla"].StringValue);
        Assert.Equal(3, report.Values["replicas"].NumberValue);
        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "replicas", 1));
    }

    [Fact]
    public void ExpressionsThatReferenceOutsideValuesAreUnknown()
    {
        var expressions = new Dictionary<string, HclExpression>
        {
            ["sla"] = HclExpression.Parse("var.tier"),
        };

        var report = new ModuleValidator(SlaModule).Validate(expressions);

        Assert.False(report.HasFailures);
        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "sla"));
    }

    [Fact]
    public void ModuleCallArgumentsAreValidated()
    {
        var module = Parse("""
            module "db" {
              source = "./db"
              sla    = "bronze"
            }
            """);

        var report = new ModuleValidator(SlaModule).Validate(module.ModuleCalls[0]);

        Assert.Equal("sla must be silver or gold.", Assert.Single(report.Failures).ErrorMessage);
    }

    [Fact]
    public void ErrorMessagesMayBeInterpolatedAndReferenceLocals()
    {
        var report = Validate(AvmModule, ("name", "\"ab\""));

        var failure = report.For("name")[0];
        Assert.Equal(ValidationOutcome.Failed, failure.Outcome);
        Assert.Equal("The name must be between 3 and 63 characters.", failure.ErrorMessage);
    }

    [Fact]
    public void ErrorMessagesMayUseFormat()
    {
        var report = Validate(AvmModule, ("name", "\"good-name\""), ("retention_days", "400"));

        Assert.Equal("retention_days must be between 1 and 365, got 400.", report.For("retention_days").Single().ErrorMessage);
    }

    [Fact]
    public void ErrorMessagesMayReferenceOtherVariables()
    {
        var report = Validate(AvmModule, ("name", "\"good-name\""), ("cidrs", "[\"10.0.0.0/8\", \"nope\"]"));

        Assert.Equal(
            "All entries must be valid CIDR blocks: 10.0.0.0/8, nope",
            report.For("cidrs").Single().ErrorMessage);
    }

    [Fact]
    public void LocalsResolveThroughOtherLocalsAndVariables()
    {
        var report = Validate(AvmModule, ("name", "\"lk-app-thing\""), ("name_prefix", "\"lk\""));

        var failure = report.For("name").Single(r => r.Index == 2);
        Assert.Equal(ValidationOutcome.Failed, failure.Outcome);
        Assert.Equal("The name must not start with lk-app.", failure.ErrorMessage);
    }

    [Fact]
    public void CanRegexAndLengthConditionsEvaluate()
    {
        var pass = Validate(AvmModule, ("name", "\"good-name\""));
        var fail = Validate(AvmModule, ("name", "\"Bad_Name\""));

        Assert.False(pass.HasFailures);
        Assert.Equal(ValidationOutcome.Failed, Outcome(fail, "name", 1));
    }

    [Fact]
    public void ObjectVariablesAreValidatedThroughAttributeAccess()
    {
        var pass = Validate(AvmModule, ("name", "\"good-name\""), ("lock", "{kind = \"ReadOnly\"}"));
        var fail = Validate(AvmModule, ("name", "\"good-name\""), ("lock", "{kind = \"Nope\"}"));
        var none = Validate(AvmModule, ("name", "\"good-name\""));

        Assert.Equal(ValidationOutcome.Passed, Outcome(pass, "lock"));
        Assert.Equal(ValidationOutcome.Failed, Outcome(fail, "lock"));
        Assert.Equal("Lock kind must be either \"CanNotDelete\" or \"ReadOnly\".", fail.For("lock").Single().ErrorMessage);
        Assert.Equal(ValidationOutcome.Passed, Outcome(none, "lock"));
    }

    [Fact]
    public void OptionalObjectAttributeDefaultsAreAppliedBeforeValidation()
    {
        var pass = Validate(AvmModule, ("name", "\"good-name\""), ("network_acl", "{}"));
        var fail = Validate(AvmModule, ("name", "\"good-name\""), ("network_acl", "{default_action = \"Maybe\"}"));

        Assert.False(pass.HasFailures);
        Assert.Equal(ValidationOutcome.Failed, Outcome(fail, "network_acl", 0));
        Assert.Equal(ValidationOutcome.Passed, Outcome(fail, "network_acl", 1));
    }

    [Fact]
    public void OptionalVariablesThatAcceptNullAreNotRequired()
    {
        var report = Validate(AvmModule, ("name", "\"good-name\""));

        Assert.False(report.IsRequiredNow("lock"));
    }

    [Fact]
    public void ForExpressionsOverSetsAndMapsEvaluate()
    {
        var guid = "\"11111111-2222-3333-4444-555555555555\"";
        var pass = Validate(AvmModule, ("name", "\"good-name\""), ("principal_ids", $"[{guid}, {guid}]"), ("tags", "{a = \"b\"}"));
        var fail = Validate(AvmModule, ("name", "\"good-name\""), ("principal_ids", "[\"x\"]"));

        Assert.False(pass.HasFailures);
        Assert.Equal(ValidationOutcome.Failed, Outcome(fail, "principal_ids"));
    }

    [Fact]
    public void UnsupportedFunctionsDataSourcesAndUnknownReferencesAreIndeterminate()
    {
        var report = Validate(AvmModule, ("name", "\"good-name\""), ("resource_id", "\"/subscriptions/x\""));

        var providerFunction = report.For("resource_id")[0];
        var dataSource = report.For("resource_id")[1];
        var workspace = report.For("resource_id")[2];

        Assert.Equal(ValidationOutcome.Indeterminate, providerFunction.Outcome);
        Assert.Equal(ValidationOutcome.Indeterminate, dataSource.Outcome);
        Assert.Equal(ValidationOutcome.Indeterminate, workspace.Outcome);
        Assert.False(string.IsNullOrEmpty(providerFunction.Reason));
        Assert.Null(providerFunction.ErrorMessage);
        Assert.False(report.HasFailures);
    }

    [Fact]
    public void ShortCircuitingKeepsUnevaluatableBranchesFromMakingTheResultIndeterminate()
    {
        var report = Validate(AvmModule, ("name", "\"good-name\""));

        Assert.All(report.For("resource_id"), r => Assert.Equal(ValidationOutcome.Passed, r.Outcome));
    }

    [Fact]
    public void HeredocErrorMessagesAreRenderedWithoutSurroundingWhitespace()
    {
        var module = Parse("""
            variable "x" {
              type = string

              validation {
                condition     = var.x == "ok"
                error_message = <<-EOT
                  x must be ok, got ${var.x}.
                EOT
              }
            }
            """);

        var report = Validate(module, ("x", "\"bad\""));

        Assert.Equal("x must be ok, got bad.", Assert.Single(report.Failures).ErrorMessage);
    }

    [Fact]
    public void LocalCyclesAreIndeterminateInsteadOfLooping()
    {
        var module = Parse("""
            locals {
              a = local.b
              b = local.a
            }

            variable "x" {
              type = string

              validation {
                condition     = local.a == var.x
                error_message = "never"
              }
            }
            """);

        var report = Validate(module, ("x", "\"v\""));

        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "x"));
    }

    [Fact]
    public void FailingLocalsMakeOnlyTheDependentValidationsIndeterminate()
    {
        var module = Parse("""
            locals {
              broken = tonumber("abc")
            }

            variable "x" {
              type = string

              validation {
                condition     = var.x != local.broken
                error_message = "uses the broken local"
              }

              validation {
                condition     = var.x != ""
                error_message = "x must not be empty"
              }
            }
            """);

        var report = Validate(module, ("x", "\"\""));

        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "x", 0));
        Assert.Equal(ValidationOutcome.Failed, Outcome(report, "x", 1));
    }

    [Fact]
    public void NonBooleanConditionsAreIndeterminate()
    {
        var module = Parse("""
            variable "x" {
              type = string

              validation {
                condition     = var.x
                error_message = "not a bool"
              }
            }
            """);

        var report = Validate(module, ("x", "\"hello\""));

        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "x"));
    }

    [Fact]
    public void StringBooleansAreAcceptedAsConditionResults()
    {
        var module = Parse("""
            variable "x" {
              type = string

              validation {
                condition     = var.x
                error_message = "not true"
              }
            }
            """);

        Assert.Equal(ValidationOutcome.Passed, Outcome(Validate(module, ("x", "\"true\"")), "x"));
        Assert.Equal(ValidationOutcome.Failed, Outcome(Validate(module, ("x", "\"false\"")), "x"));
    }

    [Fact]
    public void UnknownErrorMessageExpressionsFallBackToTheRawText()
    {
        var module = Parse("""
            variable "x" {
              type = string

              validation {
                condition     = var.x == "ok"
                error_message = "value ${data.foo.bar.id} is wrong"
              }
            }
            """);

        var report = Validate(module, ("x", "\"bad\""));

        var failure = Assert.Single(report.Failures);
        Assert.False(string.IsNullOrEmpty(failure.ErrorMessage));
    }

    [Fact]
    public void RequiredNowListsRequiredVariablesAndThoseThatCannotBeNull()
    {
        var report = Validate(SlaModule, ("sla", "\"gold\""), ("replicas", "3"));

        Assert.Equal(["sla", "backup_vault_id"], report.RequiredNow);
    }

    [Fact]
    public void ProbingTheRequiredNowCandidatesDoesNotChangeTheReportedValues()
    {
        var report = Validate(
            SlaModule,
            ("sla", "\"gold\""),
            ("replicas", "3"),
            ("backup_vault_id", "\"vault-1\""));

        Assert.True(report.IsRequiredNow("backup_vault_id"));
        Assert.Equal("vault-1", report.Values["backup_vault_id"].StringValue);
        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "backup_vault_id"));
    }

    [Fact]
    public void RequiredNowIsProbedAgainstLocalsThatDependOnTheProbedVariable()
    {
        var module = Parse("""
            variable "sla" {
              type    = string
              default = "silver"
            }

            variable "backup" {
              type    = string
              default = null

              validation {
                condition     = var.sla != "gold" || local.has_backup
                error_message = "backup is required for gold."
              }
            }

            locals {
              has_backup = var.backup != null
            }
            """);

        var report = Validate(module, ("sla", "\"gold\""), ("backup", "\"vault-1\""));

        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "backup"));
        Assert.True(report.IsRequiredNow("backup"));
    }

    [Fact]
    public void ValidationsShareTheEvaluatedLocals()
    {
        var module = Parse("""
            variable "size" {
              type    = number
              default = 5

              validation {
                condition     = local.limit >= var.size
                error_message = "size must not exceed the limit."
              }
            }

            variable "count" {
              type    = number
              default = 20

              validation {
                condition     = local.limit >= var.count
                error_message = "count must not exceed the limit."
              }
            }

            locals {
              limit = var.size * 2
            }
            """);

        var report = Validate(module);

        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "size"));
        Assert.Equal(ValidationOutcome.Failed, Outcome(report, "count"));
    }

    [Fact]
    public void ReferencedVariablesAreTheSameForEveryCall()
    {
        var validator = new ModuleValidator(SlaModule);

        var first = validator.Validate(Values(("sla", "\"gold\"")));
        var second = validator.Validate(Values(("sla", "\"silver\"")));

        Assert.Equal(["sla"], first.For("sla")[0].ReferencedVariables);
        Assert.Equal(first.For("backup_vault_id")[0].ReferencedVariables, second.For("backup_vault_id")[0].ReferencedVariables);
    }

    [Fact]
    public void IsRequiredNowAgreesWithRequiredNow()
    {
        var report = Validate(SlaModule, ("sla", "\"gold\""), ("replicas", "3"));

        foreach (var variable in SlaModule.Variables)
        {
            Assert.Equal(report.RequiredNow.Contains(variable.Name), report.IsRequiredNow(variable.Name));
        }

        Assert.False(report.IsRequiredNow("does_not_exist"));
    }

    [Fact]
    public void DefaultsAreTheSameForEveryCall()
    {
        var module = Parse("""
            variable "zones" {
              type    = list(string)
              default = concat(["a"], ["b"])

              validation {
                condition     = length(var.zones) == 2
                error_message = "two zones are expected."
              }
            }
            """);
        var validator = new ModuleValidator(module);

        var first = validator.Validate(new Dictionary<string, HclValue>());
        var overridden = validator.Validate(Values(("zones", "[\"x\"]")));
        var third = validator.Validate(new Dictionary<string, HclValue>());

        Assert.Equal(first.Values["zones"], third.Values["zones"]);
        Assert.Equal(ValidationOutcome.Passed, Outcome(first, "zones"));
        Assert.Equal(ValidationOutcome.Failed, Outcome(overridden, "zones"));
        Assert.Equal(ValidationOutcome.Passed, Outcome(third, "zones"));
    }

    [Fact]
    public void ModulesWithoutValidationsAndValuesProduceEmptyReports()
    {
        var report = Validate(TerraformModule.LoadFromDirectory("__assets__/empty-module"));

        Assert.Empty(report.Results);
        Assert.Empty(report.RequiredNow);
        Assert.False(report.HasFailures);
    }

    [Fact]
    public void ValidatorCanBeReusedAcrossCalls()
    {
        var validator = new ModuleValidator(SlaModule);

        var first = validator.Validate(Values(("sla", "\"gold\"")));
        var second = validator.Validate(Values(("sla", "\"silver\"")));
        var third = validator.Validate(Values(("sla", "\"gold\"")));

        Assert.Equal(first.RequiredNow, third.RequiredNow);
        Assert.NotEqual(first.RequiredNow, second.RequiredNow);
    }

    [Fact]
    public void ValidationLimitsProduceIndeterminateResults()
    {
        var module = Parse("""
            variable "x" {
              type = list(number)

              validation {
                condition     = alltrue([for i in var.x : i > 0])
                error_message = "positive"
              }
            }
            """);

        var report = new ModuleValidator(module, new ModuleValidatorOptions { MaxIterations = 5 })
            .Validate(Values(("x", "range(100)")));

        Assert.Equal(ValidationOutcome.Indeterminate, Outcome(report, "x"));
    }

    [Fact]
    public void CustomFunctionResolversAreUsed()
    {
        var module = Parse("""
            variable "x" {
              type = string

              validation {
                condition     = provider::custom::is_ok(var.x)
                error_message = "not ok"
              }
            }
            """);

        var resolver = new FixedResolver();
        var report = new ModuleValidator(module, new ModuleValidatorOptions { FunctionResolver = resolver })
            .Validate(Values(("x", "\"ok\"")));

        Assert.Equal(ValidationOutcome.Passed, Outcome(report, "x"));
    }

    [Fact]
    public void LocalsCanBeAddressedWithIndexSyntax()
    {
        var module = Parse("""
            locals {
              allowed = ["a", "b"]
            }

            variable "x" {
              type = string

              validation {
                condition     = contains(local["allowed"], var.x)
                error_message = "x must be one of ${join(", ", local["allowed"])}."
              }
            }
            """);

        var report = Validate(module, ("x", "\"c\""));

        Assert.Equal("x must be one of a, b.", Assert.Single(report.Failures).ErrorMessage);
    }

    [Fact]
    public async Task ValidatorCanBeUsedConcurrently()
    {
        var validator = new ModuleValidator(AvmModule);
        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            var report = validator.Validate(Values(("name", i % 2 == 0 ? "\"ab\"" : "\"good-name\"")));

            return report.HasFailures;
        }));

        var results = await Task.WhenAll(tasks);

        Assert.Equal(16, results.Count(r => r));
    }

    private sealed class FixedResolver : IHclFunctionResolver
    {
        public HclValue? Invoke(string name, IReadOnlyList<HclValue> arguments)
        {
            if (name == "provider::custom::is_ok")
            {
                return HclValue.FromBool(arguments[0].StringValue == "ok");
            }

            return null;
        }
    }
}
