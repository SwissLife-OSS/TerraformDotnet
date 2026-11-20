namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Decides how <see cref="HclFileEmitter"/> lays out expressions, mirroring how
/// <c>terraform fmt</c> treats values that open a bracket on one line and close it on another.
/// </summary>
internal static class HclExpressionLayout
{
    /// <summary>
    /// Determines whether the rendered expression spans several lines because a bracket opened
    /// on its first line is closed on a later one. <c>terraform fmt</c> does not align the
    /// <c>=</c> of such an assignment with its neighbours.
    /// A top-level heredoc is not multi-line in this sense: its first line opens no bracket.
    /// When <paramref name="comments"/> is set, a comment inside a bracket also forces it onto
    /// several lines, because a comment runs to the end of its line.
    /// </summary>
    public static bool IsMultiLine(HclExpression expression, bool comments) => expression switch
    {
        HclObjectExpression obj => obj.Elements.Count > 0 || (comments && obj.DanglingComments.Count > 0),
        HclTupleExpression tuple => ExpandsElements(tuple.Elements, tuple.DanglingComments, comments),
        HclFunctionCallExpression call =>
            call.Arguments.Exists(a => IsMultiLineElement(a, comments)) || (comments && HasComments(call)),
        HclConditionalExpression cond =>
            IsMultiLine(cond.Condition, comments)
            || IsMultiLine(cond.TrueResult, comments)
            || IsMultiLine(cond.FalseResult, comments),
        HclBinaryExpression binary => IsMultiLine(binary.Left, comments) || IsMultiLine(binary.Right, comments),
        HclUnaryExpression unary => IsMultiLine(unary.Operand, comments),
        HclForExpression forExpr =>
            IsMultiLine(forExpr.Collection, comments)
            || IsMultiLine(forExpr.ValueExpression, comments)
            || (forExpr.KeyExpression is not null && IsMultiLine(forExpr.KeyExpression, comments))
            || (forExpr.Condition is not null && IsMultiLine(forExpr.Condition, comments)),
        HclAttributeAccessExpression access => IsMultiLine(access.Source, comments),
        HclIndexExpression index => IsMultiLine(index.Collection, comments) || IsMultiLine(index.Index, comments),
        HclSplatExpression splat =>
            IsMultiLine(splat.Source, comments) || splat.Traversal.Exists(t => IsMultiLine(t, comments)),
        HclTemplateWrapExpression wrap => IsMultiLine(wrap.Wrapped, comments),
        _ => false,
    };

    /// <summary>
    /// Determines whether a tuple puts each element on its own line: an element spans lines or,
    /// when <paramref name="comments"/> is set, an element or the closing bracket has a comment.
    /// </summary>
    public static bool ExpandsElements(List<HclExpression> elements, List<HclComment> dangling, bool comments)
    {
        return elements.Exists(e => IsMultiLineElement(e, comments))
            || (comments && (dangling.Count > 0 || elements.Exists(HasComments)));
    }

    /// <summary>
    /// Determines whether a function call puts each argument on its own line. A call keeps its
    /// arguments inline and lets a single multi-line argument span the lines
    /// (<c>merge(var.a, {</c> ... <c>})</c>); it expands only when two or more arguments span lines
    /// or an argument is a heredoc, whose closing marker has to own its line. When
    /// <paramref name="comments"/> is set, a comment on an argument or before <c>)</c> expands it too.
    /// </summary>
    public static bool ExpandsArguments(HclFunctionCallExpression call, bool comments)
    {
        if (comments && HasComments(call))
        {
            return true;
        }

        int multiLine = 0;

        foreach (HclExpression argument in call.Arguments)
        {
            if (argument is HclTemplateExpression { IsHeredoc: true })
            {
                return true;
            }

            if (IsMultiLine(argument, comments) && ++multiLine > 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an element of a tuple or an argument of a function call forces the
    /// surrounding brackets onto separate lines. A heredoc does so as well, because inside a
    /// bracket its body would otherwise end up on the line of the next element.
    /// </summary>
    public static bool IsMultiLineElement(HclExpression expression, bool comments)
    {
        return IsMultiLine(expression, comments) || expression is HclTemplateExpression { IsHeredoc: true };
    }

    private static bool HasComments(HclFunctionCallExpression call)
    {
        return call.DanglingComments.Count > 0 || call.Arguments.Exists(HasComments);
    }

    private static bool HasComments(HclExpression item)
    {
        return item.LeadingComments.Count > 0 || item.TrailingComment is not null;
    }
}
