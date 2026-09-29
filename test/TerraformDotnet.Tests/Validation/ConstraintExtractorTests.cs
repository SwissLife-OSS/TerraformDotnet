using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Module;
using TerraformDotnet.Validation;

namespace TerraformDotnet.Tests.Validation;

public class ConstraintExtractorTests
{
    private static TerraformModule Parse(string hcl) =>
        TerraformModule.LoadFromContent(Encoding.UTF8.GetBytes(hcl));

    private static IReadOnlyList<VariableConstraint> ExtractCondition(string condition, string type = "string") =>
        ExtractFrom($$"""
            variable "x" {
              type = {{type}}

              validation {
                condition     = {{condition}}
                error_message = "x is invalid."
              }
            }
            """);

    private static IReadOnlyList<VariableConstraint> ExtractFrom(string hcl, string variable = "x", bool withModule = true)
    {
        var module = Parse(hcl);
        var v = module.Variables.First(v => v.Name == variable);

        return ConstraintExtractor.Extract(v, withModule ? module : null);
    }

    private static string[] Strings(IReadOnlyList<TerraformLiteral> values) =>
        [.. values.Select(v => v.Value!)];

    // ── Enum style ──────────────────────────────────────────────

    [Fact]
    public void ContainsLiteralListBecomesAllowedValues()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""contains(["silver", "gold"], var.x)""")));

        Assert.Equal(["silver", "gold"], Strings(constraint.Values));
        Assert.Equal(ConstraintTarget.Value, constraint.Target);
        Assert.Equal("x is invalid.", constraint.ErrorMessage);
        Assert.Contains("contains", constraint.Source);
    }

    [Fact]
    public void ContainsNumberListKeepsNumberKind()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("contains([1, 2, 3], var.x)", "number")));

        Assert.All(constraint.Values, v => Assert.Equal(HclLiteralKind.Number, v.Kind));
        Assert.Equal(["1", "2", "3"], Strings(constraint.Values));
    }

    [Fact]
    public void EqualityChainBecomesAllowedValues()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""var.x == "silver" || var.x == "gold" || "bronze" == var.x""")));

        Assert.Equal(["silver", "gold", "bronze"], Strings(constraint.Values));
    }

    [Fact]
    public void SingleEqualityBecomesOneAllowedValue()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""var.x == "only" """)));

        Assert.Equal(["only"], Strings(constraint.Values));
    }

    [Fact]
    public void NullGuardIsStrippedFromEnum()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""var.x == null || contains(["a", "b"], var.x)""")));

        Assert.Equal(["a", "b"], Strings(constraint.Values));
    }

    [Fact]
    public void ReversedNullGuardIsStrippedFromEnum()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""contains(["a", "b"], var.x) || null == var.x""")));

        Assert.Equal(["a", "b"], Strings(constraint.Values));
    }

    [Fact]
    public void ContainsLocalListIsResolvedWhenModuleIsProvided()
    {
        const string hcl = """
            locals {
              tiers = ["free", "paid"]
            }

            variable "x" {
              type = string

              validation {
                condition     = contains(local.tiers, var.x)
                error_message = "bad tier"
              }
            }
            """;

        var constraint = Assert.IsType<AllowedValuesConstraint>(Assert.Single(ExtractFrom(hcl)));
        Assert.Equal(["free", "paid"], Strings(constraint.Values));

        Assert.IsType<OpaqueConstraint>(Assert.Single(ExtractFrom(hcl, withModule: false)));
    }

    [Fact]
    public void ContainsKeysOfObjectLocalBecomesAllowedValues()
    {
        const string hcl = """
            locals {
              sizes = {
                small  = 1
                "big"  = 4
              }
            }

            variable "x" {
              type = string

              validation {
                condition     = contains(keys(local.sizes), var.x)
                error_message = "bad size"
              }
            }
            """;

        var constraint = Assert.IsType<AllowedValuesConstraint>(Assert.Single(ExtractFrom(hcl)));

        Assert.Equal(["small", "big"], Strings(constraint.Values));
    }

    [Fact]
    public void ContainsValuesOfObjectLiteralBecomesAllowedValues()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""contains(values({ a = "one", b = "two" }), var.x)""")));

        Assert.Equal(["one", "two"], Strings(constraint.Values));
    }

    [Fact]
    public void ContainsWithNonLiteralListIsOpaque()
    {
        Assert.IsType<OpaqueConstraint>(
            Assert.Single(ExtractCondition("""contains(["a", var.other], var.x)""")));
    }

    [Fact]
    public void EqualityChainWithNonEqualityIsOpaque()
    {
        Assert.IsType<OpaqueConstraint>(
            Assert.Single(ExtractCondition("""var.x == "a" || var.x > 3""")));
    }

    // ── Disallowed ──────────────────────────────────────────────

    [Fact]
    public void NegatedContainsBecomesDisallowedValues()
    {
        var constraint = Assert.IsType<DisallowedValuesConstraint>(
            Assert.Single(ExtractCondition("""!contains(["admin", "root"], var.x)""")));

        Assert.Equal(["admin", "root"], Strings(constraint.Values));
    }

    [Fact]
    public void NotEqualChainIsMergedIntoOneDisallowedValues()
    {
        var constraint = Assert.IsType<DisallowedValuesConstraint>(
            Assert.Single(ExtractCondition("""var.x != "admin" && var.x != "root" """)));

        Assert.Equal(["admin", "root"], Strings(constraint.Values));
        Assert.Contains("&&", constraint.Source);
    }

    [Fact]
    public void NegatedEqualityChainIsNormalisedToDisallowedValues()
    {
        var constraint = Assert.IsType<DisallowedValuesConstraint>(
            Assert.Single(ExtractCondition("""!(var.x == "admin" || var.x == "root")""")));

        Assert.Equal(["admin", "root"], Strings(constraint.Values));
    }

    // ── Numeric ranges ──────────────────────────────────────────

    [Fact]
    public void LowerAndUpperBoundAreMergedIntoOneRange()
    {
        var range = Assert.IsType<NumericRangeConstraint>(
            Assert.Single(ExtractCondition("var.x >= 1 && var.x <= 35", "number")));

        Assert.Equal(1m, range.Min);
        Assert.True(range.MinInclusive);
        Assert.Equal(35m, range.Max);
        Assert.True(range.MaxInclusive);
        Assert.Equal("x is invalid.", range.ErrorMessage);
    }

    [Fact]
    public void ExclusiveBoundsAreRepresented()
    {
        var range = Assert.IsType<NumericRangeConstraint>(
            Assert.Single(ExtractCondition("var.x > 0 && var.x < 10", "number")));

        Assert.Equal(0m, range.Min);
        Assert.False(range.MinInclusive);
        Assert.Equal(10m, range.Max);
        Assert.False(range.MaxInclusive);
    }

    [Fact]
    public void ReversedOperandsAreOriented()
    {
        var range = Assert.IsType<NumericRangeConstraint>(
            Assert.Single(ExtractCondition("1 <= var.x && 5 > var.x", "number")));

        Assert.Equal(1m, range.Min);
        Assert.True(range.MinInclusive);
        Assert.Equal(5m, range.Max);
        Assert.False(range.MaxInclusive);
    }

    [Fact]
    public void OneSidedRangeLeavesOtherBoundOpen()
    {
        var range = Assert.IsType<NumericRangeConstraint>(
            Assert.Single(ExtractCondition("var.x >= 0", "number")));

        Assert.Equal(0m, range.Min);
        Assert.Null(range.Max);
    }

    [Fact]
    public void NegatedComparisonIsNormalised()
    {
        var range = Assert.IsType<NumericRangeConstraint>(
            Assert.Single(ExtractCondition("!(var.x < 1)", "number")));

        Assert.Equal(1m, range.Min);
        Assert.True(range.MinInclusive);
    }

    [Fact]
    public void NegativeBoundIsParsed()
    {
        var range = Assert.IsType<NumericRangeConstraint>(
            Assert.Single(ExtractCondition("var.x >= -5", "number")));

        Assert.Equal(-5m, range.Min);
    }

    // ── Length, pattern, affixes, not-null ──────────────────────

    [Fact]
    public void LengthBoundsAreMerged()
    {
        var length = Assert.IsType<LengthConstraint>(
            Assert.Single(ExtractCondition("length(var.x) >= 3 && length(var.x) <= 24")));

        Assert.Equal(3, length.Min);
        Assert.Equal(24, length.Max);
        Assert.Equal(ConstraintTarget.Value, length.Target);
    }

    [Theory]
    [InlineData("length(var.x) > 0", 1, null)]
    [InlineData("length(var.x) != 0", 1, null)]
    [InlineData("length(var.x) < 10", null, 9)]
    [InlineData("length(var.x) == 4", 4, 4)]
    [InlineData("2 <= length(var.x)", 2, null)]
    public void LengthComparisonsMapToInclusiveBounds(string condition, int? min, int? max)
    {
        var length = Assert.IsType<LengthConstraint>(Assert.Single(ExtractCondition(condition)));

        Assert.Equal(min, length.Min);
        Assert.Equal(max, length.Max);
    }

    [Fact]
    public void CanRegexBecomesPattern()
    {
        var pattern = Assert.IsType<PatternConstraint>(
            Assert.Single(ExtractCondition("""can(regex("^[a-z][a-z0-9-]*$", var.x))""")));

        Assert.Equal("^[a-z][a-z0-9-]*$", pattern.Regex);
        Assert.Equal(ConstraintTarget.Value, pattern.Target);
    }

    [Theory]
    [InlineData("""length(regexall("^[a-z]+$", var.x)) > 0""")]
    [InlineData("""length(regexall("^[a-z]+$", var.x)) >= 1""")]
    [InlineData("""length(regexall("^[a-z]+$", var.x)) != 0""")]
    [InlineData("""0 < length(regexall("^[a-z]+$", var.x))""")]
    public void RegexAllLengthBecomesPattern(string condition)
    {
        var pattern = Assert.IsType<PatternConstraint>(Assert.Single(ExtractCondition(condition)));

        Assert.Equal("^[a-z]+$", pattern.Regex);
    }

    [Fact]
    public void RegexAllWithExactCountIsNotGuessed()
    {
        Assert.IsType<OpaqueConstraint>(
            Assert.Single(ExtractCondition("""length(regexall("a", var.x)) == 1""")));
    }

    [Fact]
    public void StartsWithAndEndsWithBecomeAffixConstraints()
    {
        var constraints = ExtractCondition("""startswith(var.x, "app-") && endswith(var.x, "-prod")""");

        Assert.Equal("app-", Assert.IsType<PrefixConstraint>(constraints[0]).Text);
        Assert.Equal("-prod", Assert.IsType<SuffixConstraint>(constraints[1]).Text);
    }

    [Fact]
    public void NotNullBecomesNotNullConstraint()
    {
        Assert.IsType<NotNullConstraint>(Assert.Single(ExtractCondition("var.x != null")));
    }

    [Fact]
    public void NotNullAndEnumAreBothExtracted()
    {
        var constraints = ExtractCondition("""var.x != null && contains(["a"], var.x)""");

        Assert.IsType<NotNullConstraint>(constraints[0]);
        Assert.IsType<AllowedValuesConstraint>(constraints[1]);
    }

    // ── Element constraints on list / set variables ─────────────

    [Fact]
    public void AllTrueContainsBecomesElementAllowedValues()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""alltrue([for v in var.x : contains(["a", "b"], v)])""", "set(string)")));

        Assert.Equal(ConstraintTarget.Elements, constraint.Target);
        Assert.Equal(["a", "b"], Strings(constraint.Values));
    }

    [Fact]
    public void AllTrueRegexBecomesElementPattern()
    {
        var pattern = Assert.IsType<PatternConstraint>(
            Assert.Single(ExtractCondition("""alltrue([for v in var.x : can(regex("^[a-z]+$", v))])""", "list(string)")));

        Assert.Equal(ConstraintTarget.Elements, pattern.Target);
    }

    [Fact]
    public void AllTrueWithIndexedLoopUsesValueVariable()
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(
            Assert.Single(ExtractCondition("""alltrue([for i, v in var.x : contains(["a"], v)])""", "list(string)")));

        Assert.Equal(ConstraintTarget.Elements, constraint.Target);
    }

    [Fact]
    public void AllTrueWithConjunctionYieldsOneConstraintPerRule()
    {
        var constraints = ExtractCondition(
            """alltrue([for v in var.x : length(v) >= 2 && length(v) <= 8])""",
            "list(string)");

        var length = Assert.IsType<LengthConstraint>(Assert.Single(constraints));
        Assert.Equal(ConstraintTarget.Elements, length.Target);
        Assert.Equal(2, length.Min);
        Assert.Equal(8, length.Max);
    }

    [Fact]
    public void AllTrueWithUnrecognisedBodyIsOpaque()
    {
        Assert.IsType<OpaqueConstraint>(
            Assert.Single(ExtractCondition("""alltrue([for v in var.x : can(cidrhost(v, 0))])""", "list(string)")));
    }

    [Fact]
    public void AllTrueWithFilterIsOpaque()
    {
        Assert.IsType<OpaqueConstraint>(
            Assert.Single(ExtractCondition("""alltrue([for v in var.x : contains(["a"], v) if v != ""])""", "list(string)")));
    }

    [Theory]
    [InlineData("""length(setsubtract(var.x, ["a", "b"])) == 0""")]
    [InlineData("""length(setsubtract(toset(var.x), ["a", "b"])) == 0""")]
    [InlineData("""0 == length(setsubtract(var.x, ["a", "b"]))""")]
    public void SetSubtractBecomesElementAllowedValues(string condition)
    {
        var constraint = Assert.IsType<AllowedValuesConstraint>(Assert.Single(ExtractCondition(condition, "set(string)")));

        Assert.Equal(ConstraintTarget.Elements, constraint.Target);
        Assert.Equal(["a", "b"], Strings(constraint.Values));
    }

    // ── Cross-variable rules ────────────────────────────────────

    private const string GoldVaultHcl = """
        variable "sla" {
          type = string
        }

        variable "backup_vault_id" {
          type    = string
          default = null

          validation {
            condition     = {0}
            error_message = "backup_vault_id is required for gold."
          }
        }
        """;

    [Theory]
    [InlineData("""var.sla != "gold" || var.backup_vault_id != null""")]
    [InlineData("""var.backup_vault_id != null || var.sla != "gold" """)]
    [InlineData("""var.sla == "gold" ? var.backup_vault_id != null : true""")]
    [InlineData("""var.sla != "gold" ? true : var.backup_vault_id != null""")]
    [InlineData("""!(var.sla == "gold" && var.backup_vault_id == null)""")]
    [InlineData("""var.backup_vault_id != null || !(var.sla == "gold")""")]
    public void AllSpellingsOfRequiredWhenAreRecognised(string condition)
    {
        var constraints = ExtractFrom(GoldVaultHcl.Replace("{0}", condition), "backup_vault_id");

        var required = Assert.IsType<RequiredWhenConstraint>(Assert.Single(constraints));
        var predicate = Assert.IsType<ComparePredicate>(required.Predicate);

        Assert.Equal("sla", predicate.Variable);
        Assert.Equal(CompareOperator.Equal, predicate.Operator);
        Assert.Equal("gold", predicate.Value.Value);
        Assert.Equal("backup_vault_id is required for gold.", required.ErrorMessage);
    }

    [Fact]
    public void RequiredWhenWithSeveralDriversBuildsConjunction()
    {
        var hcl = GoldVaultHcl.Replace(
            "{0}",
            """var.sla != "gold" || var.region != "eu" || var.backup_vault_id != null""");

        var required = Assert.IsType<RequiredWhenConstraint>(Assert.Single(ExtractFrom(hcl, "backup_vault_id")));
        var and = Assert.IsType<AndPredicate>(required.Predicate);

        Assert.Collection(
            and.Operands,
            o => Assert.Equal("sla", Assert.IsType<ComparePredicate>(o).Variable),
            o => Assert.Equal("region", Assert.IsType<ComparePredicate>(o).Variable));
    }

    [Fact]
    public void RequiredWhenWithContainsBuildsInPredicate()
    {
        var hcl = GoldVaultHcl.Replace(
            "{0}",
            """!contains(["gold", "platinum"], var.sla) || var.backup_vault_id != null""");

        var required = Assert.IsType<RequiredWhenConstraint>(Assert.Single(ExtractFrom(hcl, "backup_vault_id")));
        var predicate = Assert.IsType<InPredicate>(required.Predicate);

        Assert.Equal("sla", predicate.Variable);
        Assert.False(predicate.Negated);
        Assert.Equal(["gold", "platinum"], Strings(predicate.Values));
    }

    [Fact]
    public void RequiredWhenOtherIsNullBuildsIsNullPredicate()
    {
        var hcl = GoldVaultHcl.Replace(
            "{0}",
            "var.sla == null || var.backup_vault_id != null");

        var required = Assert.IsType<RequiredWhenConstraint>(Assert.Single(ExtractFrom(hcl, "backup_vault_id")));
        var predicate = Assert.IsType<IsNullPredicate>(required.Predicate);

        Assert.Equal("sla", predicate.Variable);
        Assert.True(predicate.Negated);
    }

    [Fact]
    public void RequiredWhenOnBooleanVariable()
    {
        var hcl = GoldVaultHcl.Replace("{0}", "!var.enabled || var.backup_vault_id != null");

        var required = Assert.IsType<RequiredWhenConstraint>(Assert.Single(ExtractFrom(hcl, "backup_vault_id")));
        var predicate = Assert.IsType<ComparePredicate>(required.Predicate);

        Assert.Equal("enabled", predicate.Variable);
        Assert.Equal(CompareOperator.Equal, predicate.Operator);
        Assert.Equal("true", predicate.Value.Value);
    }

    [Fact]
    public void ConditionalRangeWrapsInnerConstraint()
    {
        const string hcl = """
            variable "replicas" {
              type    = number
              default = 1

              validation {
                condition     = var.sla != "gold" || var.replicas >= 3
                error_message = "gold requires at least 3 replicas."
              }
            }
            """;

        var conditional = Assert.IsType<ConditionalConstraint>(Assert.Single(ExtractFrom(hcl, "replicas")));

        var predicate = Assert.IsType<ComparePredicate>(conditional.Predicate);
        Assert.Equal("sla", predicate.Variable);

        var range = Assert.IsType<NumericRangeConstraint>(conditional.Inner);
        Assert.Equal(3m, range.Min);
    }

    [Fact]
    public void RuleOnTheDriverIsExpressedFromItsOwnPointOfView()
    {
        const string hcl = """
            variable "sla" {
              type = string

              validation {
                condition     = var.sla != "gold" || var.backup_vault_id != null
                error_message = "gold needs a vault."
              }
            }
            """;

        var conditional = Assert.IsType<ConditionalConstraint>(Assert.Single(ExtractFrom(hcl, "sla")));

        var predicate = Assert.IsType<IsNullPredicate>(conditional.Predicate);
        Assert.Equal("backup_vault_id", predicate.Variable);
        Assert.False(predicate.Negated);

        var disallowed = Assert.IsType<DisallowedValuesConstraint>(conditional.Inner);
        Assert.Equal(["gold"], Strings(disallowed.Values));
    }

    [Fact]
    public void CrossVariableWithUnsupportedDriverIsOpaque()
    {
        var hcl = GoldVaultHcl.Replace(
            "{0}",
            "length(var.sla) < 3 || var.backup_vault_id != null");

        Assert.IsType<OpaqueConstraint>(Assert.Single(ExtractFrom(hcl, "backup_vault_id")));
    }

    [Fact]
    public void ComparingTwoVariablesIsOpaque()
    {
        var hcl = GoldVaultHcl.Replace("{0}", "var.backup_vault_id != var.sla");

        Assert.IsType<OpaqueConstraint>(Assert.Single(ExtractFrom(hcl, "backup_vault_id")));
    }

    [Fact]
    public void CrossVariableWithOpaqueConsequenceIsOpaque()
    {
        var hcl = GoldVaultHcl.Replace(
            "{0}",
            """var.sla != "gold" || can(cidrhost(var.backup_vault_id, 0))""");

        Assert.IsType<OpaqueConstraint>(Assert.Single(ExtractFrom(hcl, "backup_vault_id")));
    }

    [Fact]
    public void GetReferencedVariablesListsOtherVariables()
    {
        var module = Parse(GoldVaultHcl.Replace(
            "{0}",
            """var.sla != "gold" || var.region != "eu" || var.backup_vault_id != null"""));

        var vault = module.Variables.First(v => v.Name == "backup_vault_id");

        Assert.Equal(["sla", "region"], ConstraintExtractor.GetReferencedVariables(vault));
    }

    [Fact]
    public void GetReferencedVariablesIsEmptyWithoutCrossReferences()
    {
        var module = Parse("""
            variable "x" {
              type = string

              validation {
                condition     = contains(["a"], var.x)
                error_message = "bad"
              }
            }
            """);

        Assert.Empty(ConstraintExtractor.GetReferencedVariables(module.Variables[0]));
    }

    // ── Robustness ──────────────────────────────────────────────

    [Fact]
    public void UnrecognisedConditionIsOpaqueAndKeepsMessageAndSource()
    {
        var opaque = Assert.IsType<OpaqueConstraint>(
            Assert.Single(ExtractCondition("can(cidrhost(var.x, 0))")));

        Assert.Equal("x is invalid.", opaque.ErrorMessage);
        Assert.Equal("can(cidrhost(var.x, 0))", opaque.Source);
    }

    [Fact]
    public void OpaquePartDoesNotHideRecognisedParts()
    {
        var constraints = ExtractCondition("var.x >= 1 && can(cidrhost(\"10.0.0.0/8\", var.x))", "number");

        Assert.IsType<NumericRangeConstraint>(constraints[0]);
        Assert.IsType<OpaqueConstraint>(constraints[1]);
    }

    [Fact]
    public void FunctionCallOnTheVariableIsOpaque()
    {
        Assert.IsType<OpaqueConstraint>(
            Assert.Single(ExtractCondition("""lower(var.x) == "a" """)));
    }

    [Fact]
    public void ConditionWithoutVariableReferenceIsOpaque()
    {
        Assert.IsType<OpaqueConstraint>(Assert.Single(ExtractCondition("true")));
    }

    [Fact]
    public void VariableWithoutValidationHasNoConstraints()
    {
        var module = Parse("""
            variable "x" {
              type = string
            }
            """);

        Assert.Empty(ConstraintExtractor.Extract(module.Variables[0]));
    }

    [Fact]
    public void EachValidationBlockKeepsItsOwnMessage()
    {
        var constraints = ExtractFrom("""
            variable "x" {
              type = number

              validation {
                condition     = var.x >= 1
                error_message = "at least one"
              }

              validation {
                condition     = var.x <= 10
                error_message = "at most ten"
              }
            }
            """);

        Assert.Collection(
            constraints,
            c =>
            {
                var range = Assert.IsType<NumericRangeConstraint>(c);
                Assert.Equal("at least one", range.ErrorMessage);
                Assert.Equal(1m, range.Min);
                Assert.Null(range.Max);
            },
            c =>
            {
                var range = Assert.IsType<NumericRangeConstraint>(c);
                Assert.Equal("at most ten", range.ErrorMessage);
                Assert.Null(range.Min);
                Assert.Equal(10m, range.Max);
            });
    }

    [Fact]
    public void SelfNullCheckAloneIsOpaque()
    {
        Assert.IsType<OpaqueConstraint>(Assert.Single(ExtractCondition("var.x == null")));
    }

    [Fact]
    public void ExtractRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ConstraintExtractor.Extract(null!));
        Assert.Throws<ArgumentNullException>(() => ConstraintExtractor.ExtractAll(null!));
        Assert.Throws<ArgumentNullException>(() => ConstraintExtractor.GetReferencedVariables(null!));
    }

    // ── End to end on a realistic module ────────────────────────

    [Fact]
    public void SlaModuleEndToEnd()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/sla-module");
        var all = ConstraintExtractor.ExtractAll(module);

        var sla = Assert.IsType<AllowedValuesConstraint>(Assert.Single(all["sla"]));
        Assert.Equal(["silver", "gold"], Strings(sla.Values));

        var region = Assert.IsType<AllowedValuesConstraint>(Assert.Single(all["region"]));
        Assert.Equal(["westeurope", "switzerlandnorth"], Strings(region.Values));

        Assert.Collection(
            all["replicas"],
            c =>
            {
                var range = Assert.IsType<NumericRangeConstraint>(c);
                Assert.Equal(1m, range.Min);
                Assert.Equal(10m, range.Max);
            },
            c => Assert.IsType<ConditionalConstraint>(c));

        var vault = Assert.IsType<RequiredWhenConstraint>(Assert.Single(all["backup_vault_id"]));
        Assert.Equal("sla", Assert.IsType<ComparePredicate>(vault.Predicate).Variable);

        Assert.Empty(all["cost_center"]);
    }

    [Fact]
    public void SlaModuleRequirednessFollowsTheSelectedSla()
    {
        var module = TerraformModule.LoadFromDirectory("__assets__/sla-module");
        var vault = module.Variables.First(v => v.Name == "backup_vault_id");
        var required = Assert.IsType<RequiredWhenConstraint>(
            Assert.Single(ConstraintExtractor.Extract(vault, module)));

        Assert.True(vault.IsOptional);
        Assert.Equal(["sla"], ConstraintExtractor.GetReferencedVariables(vault));

        Assert.True(VariablePredicateEvaluator.Evaluate(
            required.Predicate,
            new Dictionary<string, TerraformLiteral> { ["sla"] = TerraformLiteral.FromString("gold") }));

        Assert.False(VariablePredicateEvaluator.Evaluate(
            required.Predicate,
            new Dictionary<string, TerraformLiteral> { ["sla"] = TerraformLiteral.FromString("silver") }));

        Assert.Null(VariablePredicateEvaluator.Evaluate(
            required.Predicate,
            new Dictionary<string, TerraformLiteral>()));
    }
}
