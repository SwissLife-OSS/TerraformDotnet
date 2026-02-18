using TerraformDotnet.Hcl.Writer;

namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Walks an <see cref="HclFile"/> AST and writes canonical HCL output
/// via a <see cref="Utf8HclWriter"/>. Produces output identical to
/// <c>terraform fmt</c>.
/// </summary>
public sealed class HclFileEmitter
{
    private readonly Utf8HclWriter _writer;
    private readonly bool _preserveComments;

    /// <summary>
    /// Initializes a new emitter that writes to the given writer.
    /// </summary>
    /// <param name="writer">The writer to emit to.</param>
    /// <param name="preserveComments">Whether to emit comments.</param>
    public HclFileEmitter(Utf8HclWriter writer, bool preserveComments = true)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _preserveComments = preserveComments;
    }

    /// <summary>
    /// Emits the entire file AST.
    /// </summary>
    /// <param name="file">The file to emit.</param>
    public void Emit(HclFile file)
    {
        EmitComments(file.LeadingComments);
        EmitBody(file.Body, isTopLevel: true);
        EmitComments(file.DanglingComments);
    }

    // ── Body & structure ────────────────────────────────────────

    private void EmitBody(HclBody body, bool isTopLevel)
    {
        // Pre-compute = alignment for attributes in this body
        var attrNames = new List<string>(body.Attributes.Count);
        foreach (HclAttribute attr in body.Attributes)
        {
            attrNames.Add(attr.Name);
        }

        Dictionary<string, int> padding = Utf8HclWriter.AlignAttributes(attrNames);

        // Build an ordered list of body elements by their Start position
        // so attributes and blocks are emitted in source order.
        var elements = new List<HclNode>(body.Attributes.Count + body.Blocks.Count);
        elements.AddRange(body.Attributes);
        elements.AddRange(body.Blocks);
        elements.Sort((a, b) => a.Start.Offset.CompareTo(b.Start.Offset));

        bool previousWasBlock = false;
        bool isFirst = true;

        foreach (HclNode element in elements)
        {
            switch (element)
            {
                case HclAttribute attr:
                    if (previousWasBlock && !isFirst)
                    {
                        _writer.WriteNewLine();
                    }

                    EmitAttribute(attr, padding.GetValueOrDefault(attr.Name, 0));
                    previousWasBlock = false;
                    break;

                case HclBlock block:
                    if (!isFirst && (isTopLevel || previousWasBlock))
                    {
                        _writer.WriteNewLine();
                    }

                    EmitBlock(block);
                    previousWasBlock = true;
                    break;
            }

            isFirst = false;
        }
    }

    private void EmitAttribute(HclAttribute attr, int namePadding)
    {
        EmitComments(attr.LeadingComments);
        _writer.WriteAttributeName(attr.Name, namePadding);
        EmitExpression(attr.Value);
        EmitTrailingComment(attr.TrailingComment);
        _writer.WriteAttributeEnd();
    }

    private void EmitBlock(HclBlock block)
    {
        EmitComments(block.LeadingComments);
        _writer.WriteBlockStart(block.Type, [.. block.Labels]);
        EmitBody(block.Body, isTopLevel: false);
        _writer.WriteBlockEnd();
    }

    // ── Expressions ─────────────────────────────────────────────

    private void EmitExpression(HclExpression expr)
    {
        switch (expr)
        {
            case HclLiteralExpression lit:
                EmitLiteral(lit);
                break;
            case HclVariableExpression variable:
                _writer.WriteRawString(variable.Name);
                break;
            case HclBinaryExpression binary:
                EmitBinary(binary);
                break;
            case HclUnaryExpression unary:
                EmitUnary(unary);
                break;
            case HclConditionalExpression cond:
                EmitConditional(cond);
                break;
            case HclFunctionCallExpression func:
                EmitFunctionCall(func);
                break;
            case HclTupleExpression tuple:
                EmitTuple(tuple);
                break;
            case HclObjectExpression obj:
                EmitObject(obj);
                break;
            case HclForExpression forExpr:
                EmitFor(forExpr);
                break;
            case HclAttributeAccessExpression attrAccess:
                EmitAttributeAccess(attrAccess);
                break;
            case HclIndexExpression index:
                EmitIndex(index);
                break;
            case HclSplatExpression splat:
                EmitSplat(splat);
                break;
            case HclTemplateExpression template:
                EmitTemplate(template);
                break;
            case HclTemplateWrapExpression templateWrap:
                EmitTemplateWrap(templateWrap);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown expression type: {expr.GetType().Name}");
        }
    }

    private void EmitLiteral(HclLiteralExpression lit)
    {
        switch (lit.Kind)
        {
            case HclLiteralKind.String:
                _writer.WriteStringValue(lit.Value ?? "");
                break;
            case HclLiteralKind.Number:
                _writer.WriteRawString(lit.Value ?? "0");
                break;
            case HclLiteralKind.Bool:
                _writer.WriteBooleanValue(string.Equals(lit.Value, "true", StringComparison.OrdinalIgnoreCase));
                break;
            case HclLiteralKind.Null:
                _writer.WriteNullValue();
                break;
        }
    }

    private void EmitBinary(HclBinaryExpression binary)
    {
        EmitExpression(binary.Left);
        _writer.WriteSpace();
        _writer.WriteRawString(OperatorToString(binary.Operator));
        _writer.WriteSpace();
        EmitExpression(binary.Right);
    }

    private void EmitUnary(HclUnaryExpression unary)
    {
        _writer.WriteRawString(unary.Operator switch
        {
            HclUnaryOperator.Negate => "-",
            HclUnaryOperator.Not => "!",
            _ => throw new InvalidOperationException($"Unknown unary operator: {unary.Operator}"),
        });
        EmitExpression(unary.Operand);
    }

    private void EmitConditional(HclConditionalExpression cond)
    {
        EmitExpression(cond.Condition);
        _writer.WriteRawHcl(" ? "u8);
        EmitExpression(cond.TrueResult);
        _writer.WriteRawHcl(" : "u8);
        EmitExpression(cond.FalseResult);
    }

    private void EmitFunctionCall(HclFunctionCallExpression func)
    {
        _writer.WriteRawString(func.Name);
        _writer.WriteRawHcl("("u8);

        for (int i = 0; i < func.Arguments.Count; i++)
        {
            if (i > 0)
            {
                _writer.WriteComma();
            }

            EmitExpression(func.Arguments[i]);

            if (func.ExpandFinalArgument && i == func.Arguments.Count - 1)
            {
                _writer.WriteRawHcl("..."u8);
            }
        }

        _writer.WriteRawHcl(")"u8);
    }

    private void EmitTuple(HclTupleExpression tuple)
    {
        _writer.WriteTupleStart();

        for (int i = 0; i < tuple.Elements.Count; i++)
        {
            if (i > 0)
            {
                _writer.WriteComma();
            }

            EmitExpression(tuple.Elements[i]);
        }

        _writer.WriteTupleEnd();
    }

    private void EmitObject(HclObjectExpression obj)
    {
        if (obj.Elements.Count == 0)
        {
            _writer.WriteObjectStart();
            _writer.WriteObjectEnd();
            return;
        }

        _writer.WriteObjectStart();

        // Compute key alignment within this object
        var keyLengths = new List<int>(obj.Elements.Count);
        foreach (HclObjectElement elem in obj.Elements)
        {
            keyLengths.Add(EstimateExpressionLength(elem.Key, elem.ForceKey));
        }

        int maxKeyLength = 0;
        foreach (int len in keyLengths)
        {
            if (len > maxKeyLength)
            {
                maxKeyLength = len;
            }
        }

        _writer.WriteNewLine();
        _writer.CurrentDepth_Increment();

        for (int i = 0; i < obj.Elements.Count; i++)
        {
            HclObjectElement elem = obj.Elements[i];
            _writer.WriteIndent();

            if (elem.ForceKey)
            {
                _writer.WriteRawHcl("("u8);
                EmitExpression(elem.Key);
                _writer.WriteRawHcl(")"u8);
            }
            else
            {
                EmitExpression(elem.Key);
            }

            int keyLen = keyLengths[i];
            int pad = maxKeyLength - keyLen;
            for (int p = 0; p < pad; p++)
            {
                _writer.WriteSpace();
            }

            if (elem.UsesColon)
            {
                _writer.WriteColon();
            }
            else
            {
                _writer.WriteEquals();
            }

            EmitExpression(elem.Value);
            _writer.WriteNewLine();
        }

        _writer.CurrentDepth_Decrement();
        _writer.WriteIndent();
        _writer.WriteObjectEnd();
    }

    private void EmitFor(HclForExpression forExpr)
    {
        _writer.WriteRawHcl(forExpr.IsObjectFor ? "{"u8 : "["u8);
        _writer.WriteRawString("for ");

        if (forExpr.ValueVariable is not null)
        {
            _writer.WriteRawString(forExpr.KeyVariable);
            _writer.WriteComma();
            _writer.WriteRawString(forExpr.ValueVariable);
        }
        else
        {
            _writer.WriteRawString(forExpr.KeyVariable);
        }

        _writer.WriteRawString(" in ");
        EmitExpression(forExpr.Collection);
        _writer.WriteRawString(" : ");

        if (forExpr.IsObjectFor && forExpr.KeyExpression is not null)
        {
            EmitExpression(forExpr.KeyExpression);
            _writer.WriteArrow();
        }

        EmitExpression(forExpr.ValueExpression);

        if (forExpr.IsGrouped)
        {
            _writer.WriteRawHcl("..."u8);
        }

        if (forExpr.Condition is not null)
        {
            _writer.WriteRawString(" if ");
            EmitExpression(forExpr.Condition);
        }

        _writer.WriteRawHcl(forExpr.IsObjectFor ? "}"u8 : "]"u8);
    }

    private void EmitAttributeAccess(HclAttributeAccessExpression attrAccess)
    {
        EmitExpression(attrAccess.Source);
        _writer.WriteRawHcl("."u8);
        _writer.WriteRawString(attrAccess.Name);
    }

    private void EmitIndex(HclIndexExpression index)
    {
        EmitExpression(index.Collection);
        if (index.IsLegacy)
        {
            _writer.WriteRawHcl("."u8);
            EmitExpression(index.Index);
        }
        else
        {
            _writer.WriteRawHcl("["u8);
            EmitExpression(index.Index);
            _writer.WriteRawHcl("]"u8);
        }
    }

    private void EmitSplat(HclSplatExpression splat)
    {
        EmitExpression(splat.Source);
        if (splat.IsFullSplat)
        {
            _writer.WriteRawHcl("[*]"u8);
        }
        else
        {
            _writer.WriteRawHcl(".*"u8);
        }

        foreach (HclExpression traversal in splat.Traversal)
        {
            switch (traversal)
            {
                case HclAttributeAccessExpression access:
                    _writer.WriteRawHcl("."u8);
                    _writer.WriteRawString(access.Name);
                    break;
                case HclIndexExpression idx:
                    _writer.WriteRawHcl("["u8);
                    EmitExpression(idx.Index);
                    _writer.WriteRawHcl("]"u8);
                    break;
            }
        }
    }

    private void EmitTemplate(HclTemplateExpression template)
    {
        if (template.IsHeredoc)
        {
            string marker = template.HeredocMarker ?? "EOF";
            if (template.IsIndented)
            {
                _writer.WriteRawString($"<<-{marker}");
            }
            else
            {
                _writer.WriteRawString($"<<{marker}");
            }

            _writer.WriteNewLine();
            _writer.WriteRawString(template.RawContent);
            _writer.WriteRawString(marker);
        }
        else
        {
            // Quoted template — emit as raw string content with the surrounding quotes
            _writer.WriteRawHcl("\""u8);
            _writer.WriteRawString(template.RawContent);
            _writer.WriteRawHcl("\""u8);
        }
    }

    private void EmitTemplateWrap(HclTemplateWrapExpression templateWrap)
    {
        _writer.WriteRawHcl("\"${"u8);
        EmitExpression(templateWrap.Wrapped);
        _writer.WriteRawHcl("}\""u8);
    }

    // ── Comments ────────────────────────────────────────────────

    private void EmitComments(List<HclComment> comments)
    {
        if (!_preserveComments || comments.Count == 0)
        {
            return;
        }

        foreach (HclComment comment in comments)
        {
            _writer.WriteIndent();
            switch (comment.Style)
            {
                case HclCommentStyle.Line:
                    _writer.WriteLineComment(comment.Text);
                    break;
                case HclCommentStyle.Hash:
                    _writer.WriteHashComment(comment.Text);
                    break;
                case HclCommentStyle.Block:
                    _writer.WriteBlockComment(comment.Text);
                    break;
            }

            _writer.WriteNewLine();
        }
    }

    private void EmitTrailingComment(HclComment? comment)
    {
        if (!_preserveComments || comment is null)
        {
            return;
        }

        _writer.WriteSpace();
        switch (comment.Style)
        {
            case HclCommentStyle.Line:
                _writer.WriteLineComment(comment.Text);
                break;
            case HclCommentStyle.Hash:
                _writer.WriteHashComment(comment.Text);
                break;
            case HclCommentStyle.Block:
                _writer.WriteBlockComment(comment.Text);
                break;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static string OperatorToString(HclBinaryOperator op) => op switch
    {
        HclBinaryOperator.Add => "+",
        HclBinaryOperator.Subtract => "-",
        HclBinaryOperator.Multiply => "*",
        HclBinaryOperator.Divide => "/",
        HclBinaryOperator.Modulo => "%",
        HclBinaryOperator.Equal => "==",
        HclBinaryOperator.NotEqual => "!=",
        HclBinaryOperator.LessThan => "<",
        HclBinaryOperator.GreaterThan => ">",
        HclBinaryOperator.LessEqual => "<=",
        HclBinaryOperator.GreaterEqual => ">=",
        HclBinaryOperator.And => "&&",
        HclBinaryOperator.Or => "||",
        _ => throw new InvalidOperationException($"Unknown operator: {op}"),
    };

    /// <summary>
    /// Estimates the rendered length of an expression (for key alignment).
    /// </summary>
    private static int EstimateExpressionLength(HclExpression expr, bool forceParen = false)
    {
        int extra = forceParen ? 2 : 0; // parens
        return extra + expr switch
        {
            HclLiteralExpression lit when lit.Kind == HclLiteralKind.String =>
                (lit.Value?.Length ?? 0) + 2, // quotes
            HclLiteralExpression lit => lit.Value?.Length ?? 0,
            HclVariableExpression v => v.Name.Length,
            _ => 10, // rough estimate for complex expressions
        };
    }
}
