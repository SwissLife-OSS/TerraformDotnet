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
    private readonly Subject _valueSubject;

    public ConstraintExtraction(
        string self,
        IReadOnlyDictionary<string, HclExpression> locals,
        string errorMessage)
    {
        _self = self;
        _locals = locals;
        _errorMessage = errorMessage;
        _valueSubject = new Subject(IsSelf, ConstraintTarget.Value);
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
            results.AddRange(ExtractConjunct(conjunct));
        }

        return Merge(results);
    }

    private List<VariableConstraint> ExtractConjunct(HclExpression conjunct)
    {
        var disjuncts = ExpressionAnalysis.FlattenOr(conjunct);

        // `var.self == null || <rest>`: the guard only says "null is fine", the rest is the real rule.
        var guardIndex = disjuncts.FindIndex(IsSelfNullCheck);

        if (guardIndex >= 0 && disjuncts.Count > 1)
        {
            disjuncts.RemoveAt(guardIndex);

            return Extract(ExpressionAnalysis.BuildBinary(HclBinaryOperator.Or, disjuncts));
        }

        var references = new List<string>();
        ExpressionAnalysis.CollectVariableReferences(conjunct, references);

        if (references.Any(name => name != _self))
        {
            return ExtractCrossVariable(conjunct, disjuncts);
        }

        if (references.Count == 0)
        {
            return [Opaque(conjunct)];
        }

        var single = TryRecognize(conjunct, _valueSubject);

        if (single is not null)
        {
            return [single];
        }

        return TryAllTrue(conjunct) ?? [Opaque(conjunct)];
    }

    /// <summary>
    /// Handles rules that mix this variable with others by rewriting <c>a || b || ...</c> into the
    /// implication <c>P =&gt; Q</c>, where <c>P</c> only talks about other variables and <c>Q</c> only about this one.
    /// </summary>
    private List<VariableConstraint> ExtractCrossVariable(HclExpression conjunct, List<HclExpression> disjuncts)
    {
        var selfParts = new List<HclExpression>();
        var otherParts = new List<HclExpression>();

        foreach (var disjunct in disjuncts)
        {
            var references = new List<string>();
            ExpressionAnalysis.CollectVariableReferences(disjunct, references);

            if (references.Count == 0)
            {
                return [Opaque(conjunct)];
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
                return [Opaque(conjunct)];
            }
        }

        if (selfParts.Count == 0 || otherParts.Count == 0)
        {
            return [Opaque(conjunct)];
        }

        // ¬(o1 ∨ o2 ...) = ¬o1 ∧ ¬o2 ...
        var negated = new List<VariablePredicate>(otherParts.Count);

        foreach (var part in otherParts)
        {
            var predicate = PredicateBuilder.TryBuild(part, _locals);

            if (predicate is null)
            {
                return [Opaque(conjunct)];
            }

            negated.Add(PredicateBuilder.Negate(predicate));
        }

        var antecedent = PredicateBuilder.MakeAnd(negated);
        var inner = Extract(ExpressionAnalysis.BuildBinary(HclBinaryOperator.Or, selfParts));

        if (inner.Any(constraint => constraint is OpaqueConstraint))
        {
            return [Opaque(conjunct)];
        }

        var source = SourceOf(conjunct);
        var results = new List<VariableConstraint>(inner.Count);

        foreach (var constraint in inner)
        {
            results.Add(constraint is NotNullConstraint
                ? new RequiredWhenConstraint(_errorMessage, source, antecedent)
                : new ConditionalConstraint(_errorMessage, source, antecedent, constraint));
        }

        return results;
    }

    private VariableConstraint? TryRecognize(HclExpression expression, Subject subject)
    {
        VariableConstraint? constraint = TryAllowedValues(expression, subject);
        constraint ??= TryDisallowedValues(expression, subject);
        constraint ??= TryNumericRange(expression, subject);
        constraint ??= TryLength(expression, subject);
        constraint ??= TryPattern(expression, subject);
        constraint ??= TryAffix(expression, subject);
        constraint ??= TryNotNull(expression, subject);
        constraint ??= TrySetSubtract(expression, subject);

        return constraint;
    }

    private AllowedValuesConstraint? TryAllowedValues(HclExpression expression, Subject subject)
    {
        if (ExpressionAnalysis.TryGetFunction(expression, "contains", 2, out var args)
            && subject.Matches(args[1])
            && ExpressionAnalysis.TryResolveLiteralList(args[0], _locals, out var listed))
        {
            return new AllowedValuesConstraint(_errorMessage, SourceOf(expression), listed, subject.Target);
        }

        // var.x == "a" || var.x == "b"
        var values = new List<TerraformLiteral>();

        foreach (var disjunct in ExpressionAnalysis.FlattenOr(expression))
        {
            if (disjunct is not HclBinaryExpression { Operator: HclBinaryOperator.Equal } equality
                || !TryGetLiteralOperand(equality, subject, out var literal)
                || literal.IsNull)
            {
                return null;
            }

            values.Add(literal);
        }

        return new AllowedValuesConstraint(_errorMessage, SourceOf(expression), values, subject.Target);
    }

    private DisallowedValuesConstraint? TryDisallowedValues(HclExpression expression, Subject subject)
    {
        if (expression is HclUnaryExpression { Operator: HclUnaryOperator.Not } not
            && ExpressionAnalysis.TryGetFunction(not.Operand, "contains", 2, out var args)
            && subject.Matches(args[1])
            && ExpressionAnalysis.TryResolveLiteralList(args[0], _locals, out var listed))
        {
            return new DisallowedValuesConstraint(_errorMessage, SourceOf(expression), listed, subject.Target);
        }

        if (expression is HclBinaryExpression { Operator: HclBinaryOperator.NotEqual } inequality
            && TryGetLiteralOperand(inequality, subject, out var literal)
            && !literal.IsNull)
        {
            return new DisallowedValuesConstraint(_errorMessage, SourceOf(expression), [literal], subject.Target);
        }

        return null;
    }

    private NumericRangeConstraint? TryNumericRange(HclExpression expression, Subject subject)
    {
        if (expression is not HclBinaryExpression binary
            || !TryOrient(binary, subject.Matches, out _, out var other, out var op)
            || !ExpressionAnalysis.TryGetNumber(other, out var number))
        {
            return null;
        }

        var source = SourceOf(expression);

        return op switch
        {
            HclBinaryOperator.GreaterThan => new NumericRangeConstraint(_errorMessage, source, number, false, null, false, subject.Target),
            HclBinaryOperator.GreaterEqual => new NumericRangeConstraint(_errorMessage, source, number, true, null, false, subject.Target),
            HclBinaryOperator.LessThan => new NumericRangeConstraint(_errorMessage, source, null, false, number, false, subject.Target),
            HclBinaryOperator.LessEqual => new NumericRangeConstraint(_errorMessage, source, null, false, number, true, subject.Target),
            _ => null,
        };
    }

    private LengthConstraint? TryLength(HclExpression expression, Subject subject)
    {
        if (expression is not HclBinaryExpression binary
            || !TryOrient(binary, side => IsLengthOf(side, subject), out _, out var other, out var op)
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

        return new LengthConstraint(_errorMessage, SourceOf(expression), (int?)min, (int?)max, subject.Target);
    }

    private PatternConstraint? TryPattern(HclExpression expression, Subject subject)
    {
        // can(regex("<pattern>", var.x))
        if (ExpressionAnalysis.TryGetFunction(expression, "can", 1, out var canArgs)
            && ExpressionAnalysis.TryGetFunction(canArgs[0], "regex", 2, out var regexArgs)
            && ExpressionAnalysis.TryGetString(regexArgs[0], out var pattern)
            && subject.Matches(regexArgs[1]))
        {
            return new PatternConstraint(_errorMessage, SourceOf(expression), pattern, subject.Target);
        }

        // length(regexall("<pattern>", var.x)) > 0
        if (expression is HclBinaryExpression binary
            && TryOrient(binary, side => TryGetRegexAll(side, subject, out _), out var matchesSide, out var other, out var op)
            && TryGetRegexAll(matchesSide, subject, out var allPattern)
            && ExpressionAnalysis.TryGetInteger(other, out var n)
            && IsAtLeastOne(op, n))
        {
            return new PatternConstraint(_errorMessage, SourceOf(expression), allPattern, subject.Target);
        }

        return null;
    }

    private VariableConstraint? TryAffix(HclExpression expression, Subject subject)
    {
        if (ExpressionAnalysis.TryGetFunction(expression, "startswith", 2, out var prefixArgs)
            && subject.Matches(prefixArgs[0])
            && ExpressionAnalysis.TryGetString(prefixArgs[1], out var prefix))
        {
            return new PrefixConstraint(_errorMessage, SourceOf(expression), prefix, subject.Target);
        }

        if (ExpressionAnalysis.TryGetFunction(expression, "endswith", 2, out var suffixArgs)
            && subject.Matches(suffixArgs[0])
            && ExpressionAnalysis.TryGetString(suffixArgs[1], out var suffix))
        {
            return new SuffixConstraint(_errorMessage, SourceOf(expression), suffix, subject.Target);
        }

        return null;
    }

    private NotNullConstraint? TryNotNull(HclExpression expression, Subject subject)
    {
        if (subject.Target == ConstraintTarget.Value
            && expression is HclBinaryExpression { Operator: HclBinaryOperator.NotEqual } inequality
            && TryGetLiteralOperand(inequality, subject, out var literal)
            && literal.IsNull)
        {
            return new NotNullConstraint(_errorMessage, SourceOf(expression));
        }

        return null;
    }

    // length(setsubtract(var.x, ["a", "b"])) == 0  →  every element is "a" or "b"
    private AllowedValuesConstraint? TrySetSubtract(HclExpression expression, Subject subject)
    {
        if (subject.Target != ConstraintTarget.Value
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
            || !IsSubjectOrConversion(subtractArgs[0], subject)
            || !ExpressionAnalysis.TryResolveLiteralList(subtractArgs[1], _locals, out var allowed))
        {
            return null;
        }

        return new AllowedValuesConstraint(_errorMessage, SourceOf(expression), allowed, ConstraintTarget.Elements);
    }

    // alltrue([for v in var.x : <rule on v>])  →  the rule applies to every element
    private List<VariableConstraint>? TryAllTrue(HclExpression expression)
    {
        if (!ExpressionAnalysis.TryGetFunction(expression, "alltrue", 1, out var args)
            || args[0] is not HclForExpression { IsObjectFor: false, Condition: null } forExpression
            || !_valueSubject.Matches(forExpression.Collection))
        {
            return null;
        }

        var loopVariable = forExpression.ValueVariable ?? forExpression.KeyVariable;
        var elementSubject = new Subject(
            candidate => candidate is HclVariableExpression variable && variable.Name == loopVariable,
            ConstraintTarget.Elements);

        var results = new List<VariableConstraint>();
        var body = ExpressionNormalizer.Normalize(forExpression.ValueExpression);

        foreach (var conjunct in ExpressionAnalysis.FlattenAnd(body))
        {
            var constraint = TryRecognize(conjunct, elementSubject);

            if (constraint is null)
            {
                return null;
            }

            results.Add(constraint);
        }

        return results;
    }

    private bool IsSelf(HclExpression expression) =>
        ExpressionAnalysis.TryGetVariableName(expression, out var name) && name == _self;

    private bool IsSelfNullCheck(HclExpression expression) =>
        expression is HclBinaryExpression { Operator: HclBinaryOperator.Equal } equality
        && TryGetLiteralOperand(equality, _valueSubject, out var literal)
        && literal.IsNull;

    private static bool IsSubjectOrConversion(HclExpression expression, Subject subject) =>
        subject.Matches(expression)
        || (expression is HclFunctionCallExpression { Name: "toset" or "tolist", Arguments.Count: 1 } conversion
            && subject.Matches(conversion.Arguments[0]));

    private static bool IsLengthOf(HclExpression expression, Subject subject) =>
        ExpressionAnalysis.TryGetFunction(expression, "length", 1, out var args) && subject.Matches(args[0]);

    private static bool TryGetRegexAll(HclExpression expression, Subject subject, out string pattern)
    {
        pattern = string.Empty;

        return ExpressionAnalysis.TryGetFunction(expression, "length", 1, out var lengthArgs)
            && ExpressionAnalysis.TryGetFunction(lengthArgs[0], "regexall", 2, out var regexArgs)
            && ExpressionAnalysis.TryGetString(regexArgs[0], out pattern)
            && subject.Matches(regexArgs[1]);
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

    private static bool TryGetLiteralOperand(HclBinaryExpression binary, Subject subject, out TerraformLiteral literal)
    {
        if (subject.Matches(binary.Left) && TerraformLiteral.TryFromExpression(binary.Right, out literal))
        {
            return true;
        }

        if (subject.Matches(binary.Right) && TerraformLiteral.TryFromExpression(binary.Left, out literal))
        {
            return true;
        }

        literal = TerraformLiteral.Null;

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
        foreach (var target in Enum.GetValues<ConstraintTarget>())
        {
            MergeDisallowed(constraints, target);
            MergeBounds(
                constraints,
                c => c is NumericRangeConstraint { Min: not null, Max: null } r && r.Target == target,
                c => c is NumericRangeConstraint { Min: null, Max: not null } r && r.Target == target,
                (lower, upper) =>
                {
                    var low = (NumericRangeConstraint)lower;
                    var high = (NumericRangeConstraint)upper;

                    return low with
                    {
                        Source = $"{low.Source} && {high.Source}",
                        Max = high.Max,
                        MaxInclusive = high.MaxInclusive,
                    };
                });
            MergeBounds(
                constraints,
                c => c is LengthConstraint { Min: not null, Max: null } l && l.Target == target,
                c => c is LengthConstraint { Min: null, Max: not null } l && l.Target == target,
                (lower, upper) =>
                {
                    var low = (LengthConstraint)lower;
                    var high = (LengthConstraint)upper;

                    return low with { Source = $"{low.Source} && {high.Source}", Max = high.Max };
                });
        }

        return constraints;
    }

    private static void MergeDisallowed(List<VariableConstraint> constraints, ConstraintTarget target)
    {
        var matches = constraints
            .OfType<DisallowedValuesConstraint>()
            .Where(c => c.Target == target)
            .ToList();

        if (matches.Count < 2)
        {
            return;
        }

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

    private static void MergeBounds(
        List<VariableConstraint> constraints,
        Func<VariableConstraint, bool> isLower,
        Func<VariableConstraint, bool> isUpper,
        Func<VariableConstraint, VariableConstraint, VariableConstraint> combine)
    {
        var lower = constraints.FirstOrDefault(isLower);
        var upper = constraints.FirstOrDefault(isUpper);

        if (lower is null || upper is null)
        {
            return;
        }

        constraints[constraints.IndexOf(lower)] = combine(lower, upper);
        constraints.Remove(upper);
    }

    /// <summary>Identifies which expression a constraint talks about: the variable itself or a loop element.</summary>
    private readonly record struct Subject(Func<HclExpression, bool> Matches, ConstraintTarget Target);
}
