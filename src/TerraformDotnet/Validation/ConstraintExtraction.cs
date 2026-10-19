using TerraformDotnet.Emit;
using TerraformDotnet.Hcl.Nodes;

namespace TerraformDotnet.Validation;

/// <summary>
/// Extracts the <see cref="VariableConstraint"/>s of a single validation condition. One instance is
/// bound to one variable, one set of module locals and one error message.
/// </summary>
internal sealed class ConstraintExtraction
{
    private readonly string _self;
    private readonly IReadOnlyDictionary<string, HclExpression> _locals;
    private readonly string _errorMessage;

    public ConstraintExtraction(
        string self,
        IReadOnlyDictionary<string, HclExpression> locals,
        string errorMessage)
    {
        _self = self;
        _locals = locals;
        _errorMessage = errorMessage;
    }

    /// <summary>
    /// Extracts constraints from a (not yet normalised) condition. Every top-level <c>&amp;&amp;</c> operand
    /// is analysed on its own; operands that cannot be classified become <see cref="OpaqueConstraint"/>s.
    /// </summary>
    public List<VariableConstraint> Extract(HclExpression condition)
    {
        var results = new List<VariableConstraint>();

        foreach (var conjunct in ExpressionAnalysis.FlattenAnd(ExpressionNormalizer.Normalize(condition)))
        {
            results.AddRange(ExtractConjunct(conjunct, loopVariable: null) ?? [Opaque(conjunct)]);
        }

        return Merge(results);
    }

    /// <summary>
    /// Extracts constraints inside a nested scope (such as a null-guarded branch, cross-variable
    /// consequent, or <c>alltrue</c> loop body), returning <c>null</c> when any conjunct cannot be classified.
    /// </summary>
    private List<VariableConstraint>? ExtractInScope(HclExpression expression, string? loopVariable)
    {
        var results = new List<VariableConstraint>();

        foreach (var conjunct in ExpressionAnalysis.FlattenAnd(ExpressionNormalizer.Normalize(expression)))
        {
            var extracted = ExtractConjunct(conjunct, loopVariable);

            if (extracted is null || extracted.Any(c => c is OpaqueConstraint))
            {
                return null;
            }

            results.AddRange(extracted);
        }

        return Merge(results);
    }

    private List<VariableConstraint>? ExtractConjunct(HclExpression conjunct, string? loopVariable)
    {
        var disjuncts = ExpressionAnalysis.FlattenOr(conjunct);

        // `var.self == null || <rest>` or `r.field == null || <rest>`: the guard only says "null is fine",
        // the rest is the real rule as long as any field-specific guard matches the constrained field.
        if (disjuncts.Count > 1
            && TryStripNullGuards(disjuncts, loopVariable, out var remaining, out var guardedFields))
        {
            var rest = ExpressionAnalysis.BuildBinary(HclBinaryOperator.Or, remaining);
            var guarded = ExtractInScope(rest, loopVariable);

            if (guarded is { Count: > 0 } && GuardsMatchFields(guardedFields, guarded))
            {
                return guarded;
            }
        }

        if (loopVariable is null)
        {
            var references = new List<string>();
            ExpressionAnalysis.CollectVariableReferences(conjunct, references);

            if (references.Any(name => name != _self))
            {
                return ExtractCrossVariable(conjunct, disjuncts);
            }

            if (references.Count == 0)
            {
                return null;
            }
        }

        var single = TryRecognize(conjunct, loopVariable);

        if (single is not null)
        {
            return [single];
        }

        return loopVariable is null ? TryAllTrue(conjunct) : null;
    }

    private bool TryStripNullGuards(
        List<HclExpression> disjuncts,
        string? loopVariable,
        out List<HclExpression> remaining,
        out HashSet<string?> guardedFields)
    {
        remaining = [];
        guardedFields = new HashSet<string?>(StringComparer.Ordinal);

        foreach (var disjunct in disjuncts)
        {
            if (IsNullGuard(disjunct, loopVariable, out var fieldName))
            {
                guardedFields.Add(fieldName);
            }
            else
            {
                remaining.Add(disjunct);
            }
        }

        return guardedFields.Count > 0 && remaining.Count > 0;
    }

    private static bool GuardsMatchFields(
        HashSet<string?> guardedFields,
        List<VariableConstraint> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is NotNullConstraint && guardedFields.Contains(candidate.FieldName))
            {
                return false;
            }
        }

        foreach (var guardedField in guardedFields)
        {
            if (guardedField is not null
                && candidates.Any(c => !string.Equals(c.FieldName, guardedField, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsNullGuard(HclExpression expression, string? loopVariable, out string? fieldName)
    {
        if (expression is HclBinaryExpression { Operator: HclBinaryOperator.Equal } equality
            && TryGetLiteralOperand(equality, loopVariable, out var literal, out var subject)
            && literal.IsNull
            && !subject.HasWrapper)
        {
            fieldName = subject.FieldName;

            return true;
        }

        fieldName = null;

        return false;
    }

    /// <summary>
    /// Handles rules that mix this variable with others by rewriting <c>a || b || ...</c> into the
    /// implication <c>P =&gt; Q</c>, where <c>P</c> only talks about other variables and <c>Q</c> only about this one.
    /// </summary>
    private List<VariableConstraint>? ExtractCrossVariable(HclExpression conjunct, List<HclExpression> disjuncts)
    {
        var selfParts = new List<HclExpression>();
        var otherParts = new List<HclExpression>();

        foreach (var disjunct in disjuncts)
        {
            var references = new List<string>();
            ExpressionAnalysis.CollectVariableReferences(disjunct, references);

            if (references.Count == 0)
            {
                return null;
            }

            if (references.All(name => name == _self))
            {
                selfParts.Add(disjunct);
            }
            else if (references.All(name => name != _self))
            {
                otherParts.Add(disjunct);
            }
            else
            {
                return null;
            }
        }

        if (selfParts.Count == 0 || otherParts.Count == 0)
        {
            return null;
        }

        // ¬(o1 ∨ o2 ...) = ¬o1 ∧ ¬o2 ...
        var negated = new List<VariablePredicate>(otherParts.Count);

        foreach (var part in otherParts)
        {
            var predicate = PredicateBuilder.TryBuild(part, _locals);

            if (predicate is null)
            {
                return null;
            }

            negated.Add(PredicateBuilder.Negate(predicate));
        }

        var antecedent = PredicateBuilder.MakeAnd(negated);
        var inner = ExtractInScope(ExpressionAnalysis.BuildBinary(HclBinaryOperator.Or, selfParts), loopVariable: null);

        if (inner is null || inner.Count == 0)
        {
            return null;
        }

        var source = SourceOf(conjunct);
        var results = new List<VariableConstraint>(inner.Count);

        foreach (var constraint in inner)
        {
            results.Add(constraint is NotNullConstraint notNull
                ? new RequiredWhenConstraint(_errorMessage, source, antecedent, notNull.FieldName)
                : new ConditionalConstraint(_errorMessage, source, antecedent, constraint, constraint.FieldName));
        }

        return results;
    }

    private VariableConstraint? TryRecognize(HclExpression expression, string? loopVariable)
    {
        VariableConstraint? constraint = TryAllowedValues(expression, loopVariable);
        constraint ??= TryDisallowedValues(expression, loopVariable);
        constraint ??= TryNumericRange(expression, loopVariable);
        constraint ??= TryLength(expression, loopVariable);
        constraint ??= TryPattern(expression, loopVariable);
        constraint ??= TryAffix(expression, loopVariable);
        constraint ??= TryNotNull(expression, loopVariable);
        constraint ??= TrySetSubtract(expression, loopVariable);

        return constraint;
    }

    private AllowedValuesConstraint? TryAllowedValues(HclExpression expression, string? loopVariable)
    {
        if (ExpressionAnalysis.TryGetFunction(expression, "contains", 2, out var args)
            && TryMatchSubject(args[1], loopVariable, out var subject)
            && ExpressionAnalysis.TryResolveLiteralList(args[0], _locals, out var listed))
        {
            return new AllowedValuesConstraint(_errorMessage, SourceOf(expression), listed, subject.Target, subject.FieldName);
        }

        // var.x == "a" || var.x == "b"
        var values = new List<TerraformLiteral>();
        SubjectMatch? commonSubject = null;

        foreach (var disjunct in ExpressionAnalysis.FlattenOr(expression))
        {
            if (disjunct is not HclBinaryExpression { Operator: HclBinaryOperator.Equal } equality
                || !TryGetLiteralOperand(equality, loopVariable, out var literal, out var disjunctSubject)
                || literal.IsNull)
            {
                return null;
            }

            if (commonSubject is { } existing && !SameSubjectTarget(existing, disjunctSubject))
            {
                return null;
            }

            commonSubject = disjunctSubject;
            values.Add(literal);
        }

        if (commonSubject is not { } matched || values.Count == 0)
        {
            return null;
        }

        return new AllowedValuesConstraint(_errorMessage, SourceOf(expression), values, matched.Target, matched.FieldName);
    }

    private DisallowedValuesConstraint? TryDisallowedValues(HclExpression expression, string? loopVariable)
    {
        if (expression is HclUnaryExpression { Operator: HclUnaryOperator.Not } not
            && ExpressionAnalysis.TryGetFunction(not.Operand, "contains", 2, out var args)
            && TryMatchSubject(args[1], loopVariable, out var subject)
            && ExpressionAnalysis.TryResolveLiteralList(args[0], _locals, out var listed))
        {
            return new DisallowedValuesConstraint(_errorMessage, SourceOf(expression), listed, subject.Target, subject.FieldName);
        }

        if (expression is HclBinaryExpression { Operator: HclBinaryOperator.NotEqual } inequality
            && TryGetLiteralOperand(inequality, loopVariable, out var literal, out var neqSubject)
            && !literal.IsNull)
        {
            return new DisallowedValuesConstraint(_errorMessage, SourceOf(expression), [literal], neqSubject.Target, neqSubject.FieldName);
        }

        return null;
    }

    private NumericRangeConstraint? TryNumericRange(HclExpression expression, string? loopVariable)
    {
        if (expression is not HclBinaryExpression binary
            || !TryOrient(binary, side => TryMatchSubject(side, loopVariable, out _), out var subjectSide, out var other, out var op)
            || !TryMatchSubject(subjectSide, loopVariable, out var subject)
            || !ExpressionAnalysis.TryGetNumber(other, out var number))
        {
            return null;
        }

        var source = SourceOf(expression);

        return op switch
        {
            HclBinaryOperator.GreaterThan => new NumericRangeConstraint(_errorMessage, source, number, false, null, false, subject.Target, subject.FieldName),
            HclBinaryOperator.GreaterEqual => new NumericRangeConstraint(_errorMessage, source, number, true, null, false, subject.Target, subject.FieldName),
            HclBinaryOperator.LessThan => new NumericRangeConstraint(_errorMessage, source, null, false, number, false, subject.Target, subject.FieldName),
            HclBinaryOperator.LessEqual => new NumericRangeConstraint(_errorMessage, source, null, false, number, true, subject.Target, subject.FieldName),
            _ => null,
        };
    }

    private LengthConstraint? TryLength(HclExpression expression, string? loopVariable)
    {
        if (expression is not HclBinaryExpression binary
            || !TryOrient(binary, side => TryMatchLengthOf(side, loopVariable, out _), out var lengthSide, out var other, out var op)
            || !TryMatchLengthOf(lengthSide, loopVariable, out var subject)
            || !ExpressionAnalysis.TryGetInteger(other, out var n))
        {
            return null;
        }

        // Widen so `> int.MaxValue` style bounds cannot overflow before the range check below.
        long value = n;
        long? min = null;
        long? max = null;

        switch (op)
        {
            case HclBinaryOperator.GreaterEqual:
                min = value;
                break;
            case HclBinaryOperator.GreaterThan:
                min = value + 1;
                break;
            case HclBinaryOperator.LessEqual:
                max = value;
                break;
            case HclBinaryOperator.LessThan:
                max = value - 1;
                break;
            case HclBinaryOperator.Equal:
                min = value;
                max = value;
                break;
            case HclBinaryOperator.NotEqual when value == 0:
                min = 1;
                break;
            default:
                return null;
        }

        if (min is > int.MaxValue || max is < int.MinValue)
        {
            return null;
        }

        return new LengthConstraint(_errorMessage, SourceOf(expression), (int?)min, (int?)max, subject.Target, subject.FieldName);
    }

    private PatternConstraint? TryPattern(HclExpression expression, string? loopVariable)
    {
        // can(regex("<pattern>", var.x))
        if (ExpressionAnalysis.TryGetFunction(expression, "can", 1, out var canArgs)
            && ExpressionAnalysis.TryGetFunction(canArgs[0], "regex", 2, out var regexArgs)
            && ExpressionAnalysis.TryGetString(regexArgs[0], out var pattern)
            && TryMatchSubject(regexArgs[1], loopVariable, out var subject))
        {
            return new PatternConstraint(_errorMessage, SourceOf(expression), pattern, subject.Target, subject.FieldName);
        }

        // length(regexall("<pattern>", var.x)) > 0
        if (expression is HclBinaryExpression binary
            && TryOrient(binary, side => TryGetRegexAll(side, loopVariable, out _, out _), out var matchesSide, out var other, out var op)
            && TryGetRegexAll(matchesSide, loopVariable, out var allPattern, out var regexSubject)
            && ExpressionAnalysis.TryGetInteger(other, out var n)
            && IsAtLeastOne(op, n))
        {
            return new PatternConstraint(_errorMessage, SourceOf(expression), allPattern, regexSubject.Target, regexSubject.FieldName);
        }

        return null;
    }

    private VariableConstraint? TryAffix(HclExpression expression, string? loopVariable)
    {
        if (ExpressionAnalysis.TryGetFunction(expression, "startswith", 2, out var prefixArgs)
            && TryMatchSubject(prefixArgs[0], loopVariable, out var prefixSubject)
            && ExpressionAnalysis.TryGetString(prefixArgs[1], out var prefix))
        {
            return new PrefixConstraint(_errorMessage, SourceOf(expression), prefix, prefixSubject.Target, prefixSubject.FieldName);
        }

        if (ExpressionAnalysis.TryGetFunction(expression, "endswith", 2, out var suffixArgs)
            && TryMatchSubject(suffixArgs[0], loopVariable, out var suffixSubject)
            && ExpressionAnalysis.TryGetString(suffixArgs[1], out var suffix))
        {
            return new SuffixConstraint(_errorMessage, SourceOf(expression), suffix, suffixSubject.Target, suffixSubject.FieldName);
        }

        return null;
    }

    private NotNullConstraint? TryNotNull(HclExpression expression, string? loopVariable)
    {
        if (loopVariable is null
            && expression is HclBinaryExpression { Operator: HclBinaryOperator.NotEqual } inequality
            && TryGetLiteralOperand(inequality, loopVariable: null, out var literal, out var subject)
            && literal.IsNull
            && !subject.HasWrapper)
        {
            return new NotNullConstraint(_errorMessage, SourceOf(expression), subject.FieldName);
        }

        return null;
    }

    // length(setsubtract(var.x, ["a", "b"])) == 0  →  every element is "a" or "b"
    private AllowedValuesConstraint? TrySetSubtract(HclExpression expression, string? loopVariable)
    {
        if (loopVariable is not null
            || expression is not HclBinaryExpression { Operator: HclBinaryOperator.Equal } equality)
        {
            return null;
        }

        var (call, number) = ExpressionAnalysis.TryGetInteger(equality.Right, out _)
            ? (equality.Left, equality.Right)
            : (equality.Right, equality.Left);

        if (!ExpressionAnalysis.TryGetInteger(number, out var zero)
            || zero != 0
            || !ExpressionAnalysis.TryGetFunction(call, "length", 1, out var lengthArgs)
            || !ExpressionAnalysis.TryGetFunction(lengthArgs[0], "setsubtract", 2, out var subtractArgs)
            || !TryMatchSubject(subtractArgs[0], loopVariable: null, out var subject)
            || !ExpressionAnalysis.TryResolveLiteralList(subtractArgs[1], _locals, out var allowed))
        {
            return null;
        }

        return new AllowedValuesConstraint(_errorMessage, SourceOf(expression), allowed, ConstraintTarget.Elements, subject.FieldName);
    }

    // alltrue([for v in var.x : <rule on v>])  →  the rule applies to every element
    private List<VariableConstraint>? TryAllTrue(HclExpression expression)
    {
        if (!ExpressionAnalysis.TryGetFunction(expression, "alltrue", 1, out var args)
            || args[0] is not HclForExpression { IsObjectFor: false } forExpression
            || !TryMatchSubject(forExpression.Collection, loopVariable: null, out var collectionSubject)
            || collectionSubject is not { Target: ConstraintTarget.Value, FieldName: null, HasWrapper: false })
        {
            return null;
        }

        var loopVariable = forExpression.ValueVariable ?? forExpression.KeyVariable;
        string? conditionField = null;

        if (forExpression.Condition is not null
            && !IsLoopNotNullCondition(forExpression.Condition, loopVariable, out conditionField))
        {
            return null;
        }

        var extracted = ExtractInScope(forExpression.ValueExpression, loopVariable);

        if (extracted is null || extracted.Count == 0)
        {
            return null;
        }

        if (conditionField is not null
            && extracted.Any(c => !string.Equals(c.FieldName, conditionField, StringComparison.Ordinal)))
        {
            return null;
        }

        return extracted;
    }

    private bool IsLoopNotNullCondition(HclExpression condition, string loopVariable, out string? fieldName)
    {
        if (condition is HclBinaryExpression { Operator: HclBinaryOperator.NotEqual } inequality
            && TryGetLiteralOperand(inequality, loopVariable, out var literal, out var subject)
            && literal.IsNull
            && !subject.HasWrapper)
        {
            fieldName = subject.FieldName;

            return true;
        }

        fieldName = null;

        return false;
    }

    private bool TryMatchSubject(
        HclExpression expression,
        string? loopVariable,
        out SubjectMatch subject)
    {
        var current = UnwrapSubjectConversions(expression, out var hasWrapper);

        return loopVariable is not null
            ? TryMatchLoopSubject(current, loopVariable, hasWrapper, out subject)
            : TryMatchVariableSubject(current, hasWrapper, out subject);
    }

    private static HclExpression UnwrapSubjectConversions(HclExpression expression, out bool hasWrapper)
    {
        hasWrapper = false;
        var current = expression;

        while (current is HclFunctionCallExpression { ExpandFinalArgument: false, Arguments.Count: 1 } call)
        {
            switch (call.Name)
            {
                case "upper" or "lower" or "trimspace":
                    hasWrapper = true;
                    current = call.Arguments[0];
                    break;

                case "toset" or "tolist":
                    current = call.Arguments[0];
                    break;

                default:
                    return current;
            }
        }

        return current;
    }

    private static bool TryMatchLoopSubject(
        HclExpression current,
        string loopVariable,
        bool hasWrapper,
        out SubjectMatch subject)
    {
        if (current is HclVariableExpression { Name: var loopName }
            && string.Equals(loopName, loopVariable, StringComparison.Ordinal))
        {
            subject = new SubjectMatch(ConstraintTarget.Elements, null, hasWrapper);

            return true;
        }

        if (current is HclAttributeAccessExpression
            {
                Source: HclVariableExpression { Name: var sourceLoopName },
                Name: var elementFieldName,
            }
            && string.Equals(sourceLoopName, loopVariable, StringComparison.Ordinal))
        {
            subject = new SubjectMatch(ConstraintTarget.Elements, elementFieldName, hasWrapper);

            return true;
        }

        subject = default;

        return false;
    }

    private bool TryMatchVariableSubject(
        HclExpression current,
        bool hasWrapper,
        out SubjectMatch subject)
    {
        if (ExpressionAnalysis.TryGetVariableName(current, out var varName)
            && string.Equals(varName, _self, StringComparison.Ordinal))
        {
            subject = new SubjectMatch(ConstraintTarget.Value, null, hasWrapper);

            return true;
        }

        if (current is HclAttributeAccessExpression { Source: var attrSource, Name: var fieldName }
            && ExpressionAnalysis.TryGetVariableName(attrSource, out var ownerVarName)
            && string.Equals(ownerVarName, _self, StringComparison.Ordinal))
        {
            subject = new SubjectMatch(ConstraintTarget.Value, fieldName, hasWrapper);

            return true;
        }

        subject = default;

        return false;
    }

    private static bool SameSubjectTarget(SubjectMatch first, SubjectMatch second) =>
        first.Target == second.Target
        && string.Equals(first.FieldName, second.FieldName, StringComparison.Ordinal);

    private bool TryMatchLengthOf(HclExpression expression, string? loopVariable, out SubjectMatch subject)
    {
        if (ExpressionAnalysis.TryGetFunction(expression, "length", 1, out var args)
            && TryMatchSubject(args[0], loopVariable, out subject))
        {
            return true;
        }

        subject = default;

        return false;
    }

    private bool TryGetRegexAll(
        HclExpression expression,
        string? loopVariable,
        out string pattern,
        out SubjectMatch subject)
    {
        pattern = string.Empty;
        subject = default;

        return ExpressionAnalysis.TryGetFunction(expression, "length", 1, out var lengthArgs)
            && ExpressionAnalysis.TryGetFunction(lengthArgs[0], "regexall", 2, out var regexArgs)
            && ExpressionAnalysis.TryGetString(regexArgs[0], out pattern)
            && TryMatchSubject(regexArgs[1], loopVariable, out subject);
    }

    /// <summary>Whether <c>length(matches) op n</c> means "at least one match".</summary>
    private static bool IsAtLeastOne(HclBinaryOperator op, int n) => (op, n) switch
    {
        (HclBinaryOperator.GreaterThan, 0) => true,
        (HclBinaryOperator.GreaterEqual, 1) => true,
        (HclBinaryOperator.NotEqual, 0) => true,
        _ => false,
    };

    /// <summary>
    /// Orients a comparison so the operand accepted by <paramref name="isSubjectSide"/> is on the left,
    /// swapping <c>&lt;</c>/<c>&gt;</c> when the operands had to be exchanged.
    /// </summary>
    private static bool TryOrient(
        HclBinaryExpression binary,
        Func<HclExpression, bool> isSubjectSide,
        out HclExpression subjectSide,
        out HclExpression other,
        out HclBinaryOperator op)
    {
        if (isSubjectSide(binary.Left))
        {
            subjectSide = binary.Left;
            other = binary.Right;
            op = binary.Operator;

            return true;
        }

        if (isSubjectSide(binary.Right))
        {
            subjectSide = binary.Right;
            other = binary.Left;
            op = binary.Operator switch
            {
                HclBinaryOperator.LessThan => HclBinaryOperator.GreaterThan,
                HclBinaryOperator.LessEqual => HclBinaryOperator.GreaterEqual,
                HclBinaryOperator.GreaterThan => HclBinaryOperator.LessThan,
                HclBinaryOperator.GreaterEqual => HclBinaryOperator.LessEqual,
                var symmetric => symmetric,
            };

            return true;
        }

        subjectSide = binary.Left;
        other = binary.Left;
        op = binary.Operator;

        return false;
    }

    private bool TryGetLiteralOperand(
        HclBinaryExpression binary,
        string? loopVariable,
        out TerraformLiteral literal,
        out SubjectMatch subject)
    {
        if (TryMatchSubject(binary.Left, loopVariable, out subject)
            && TerraformLiteral.TryFromExpression(binary.Right, out literal))
        {
            return true;
        }

        if (TryMatchSubject(binary.Right, loopVariable, out subject)
            && TerraformLiteral.TryFromExpression(binary.Left, out literal))
        {
            return true;
        }

        literal = TerraformLiteral.Null;
        subject = default;

        return false;
    }

    private OpaqueConstraint Opaque(HclExpression expression) => new(_errorMessage, SourceOf(expression));

    private static string SourceOf(HclExpression expression) => ModuleCallEmitter.EmitExpression(expression);

    /// <summary>
    /// Combines constraints that the author split across <c>&amp;&amp;</c> but that describe one rule:
    /// several "not equal" checks, and a lower plus an upper bound.
    /// </summary>
    private static List<VariableConstraint> Merge(List<VariableConstraint> constraints)
    {
        MergeDisallowed(constraints);
        MergeNumericBounds(constraints);
        MergeLengthBounds(constraints);

        return constraints;
    }

    private static void MergeDisallowed(List<VariableConstraint> constraints)
    {
        var groups = constraints
            .OfType<DisallowedValuesConstraint>()
            .GroupBy(c => (c.Target, c.FieldName))
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var group in groups)
        {
            var matches = group.ToList();
            var merged = matches[0] with
            {
                Source = string.Join(" && ", matches.Select(c => c.Source)),
                Values = [.. matches.SelectMany(c => c.Values)],
            };

            constraints[constraints.IndexOf(matches[0])] = merged;

            foreach (var duplicate in matches.Skip(1))
            {
                constraints.Remove(duplicate);
            }
        }
    }

    private static void MergeNumericBounds(List<VariableConstraint> constraints)
    {
        for (var i = 0; i < constraints.Count; i++)
        {
            if (constraints[i] is not NumericRangeConstraint { Min: not null, Max: null } low)
            {
                continue;
            }

            var upperIndex = constraints.FindIndex(
                c => c is NumericRangeConstraint { Min: null, Max: not null } high
                    && high.Target == low.Target
                    && string.Equals(high.FieldName, low.FieldName, StringComparison.Ordinal));

            if (upperIndex < 0)
            {
                continue;
            }

            var upper = (NumericRangeConstraint)constraints[upperIndex];
            constraints[i] = low with
            {
                Source = $"{low.Source} && {upper.Source}",
                Max = upper.Max,
                MaxInclusive = upper.MaxInclusive,
            };
            constraints.RemoveAt(upperIndex);

            if (upperIndex < i)
            {
                i--;
            }
        }
    }

    private static void MergeLengthBounds(List<VariableConstraint> constraints)
    {
        for (var i = 0; i < constraints.Count; i++)
        {
            if (constraints[i] is not LengthConstraint { Min: not null, Max: null } low)
            {
                continue;
            }

            var upperIndex = constraints.FindIndex(
                c => c is LengthConstraint { Min: null, Max: not null } high
                    && high.Target == low.Target
                    && string.Equals(high.FieldName, low.FieldName, StringComparison.Ordinal));

            if (upperIndex < 0)
            {
                continue;
            }

            var upper = (LengthConstraint)constraints[upperIndex];
            constraints[i] = low with
            {
                Source = $"{low.Source} && {upper.Source}",
                Max = upper.Max,
            };
            constraints.RemoveAt(upperIndex);

            if (upperIndex < i)
            {
                i--;
            }
        }
    }

    /// <summary>Identifies which expression a constraint talks about: the variable, an object attribute, or a loop element.</summary>
    private readonly record struct SubjectMatch(ConstraintTarget Target, string? FieldName, bool HasWrapper);
}
