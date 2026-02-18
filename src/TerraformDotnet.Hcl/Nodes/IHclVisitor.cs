namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Visitor interface for traversing the HCL AST.
/// </summary>
public interface IHclVisitor
{
    /// <summary>Visits an <see cref="HclFile"/> node.</summary>
    void VisitFile(HclFile file);

    /// <summary>Visits an <see cref="HclBody"/> node.</summary>
    void VisitBody(HclBody body);

    /// <summary>Visits an <see cref="HclBlock"/> node.</summary>
    void VisitBlock(HclBlock block);

    /// <summary>Visits an <see cref="HclAttribute"/> node.</summary>
    void VisitAttribute(HclAttribute attribute);

    /// <summary>Visits an <see cref="HclLiteralExpression"/> node.</summary>
    void VisitLiteral(HclLiteralExpression literal);

    /// <summary>Visits an <see cref="HclTemplateExpression"/> node.</summary>
    void VisitTemplate(HclTemplateExpression template);

    /// <summary>Visits an <see cref="HclTemplateWrapExpression"/> node.</summary>
    void VisitTemplateWrap(HclTemplateWrapExpression templateWrap);

    /// <summary>Visits an <see cref="HclVariableExpression"/> node.</summary>
    void VisitVariable(HclVariableExpression variable);

    /// <summary>Visits an <see cref="HclIndexExpression"/> node.</summary>
    void VisitIndex(HclIndexExpression index);

    /// <summary>Visits an <see cref="HclAttributeAccessExpression"/> node.</summary>
    void VisitAttributeAccess(HclAttributeAccessExpression attributeAccess);

    /// <summary>Visits an <see cref="HclSplatExpression"/> node.</summary>
    void VisitSplat(HclSplatExpression splat);

    /// <summary>Visits an <see cref="HclUnaryExpression"/> node.</summary>
    void VisitUnary(HclUnaryExpression unary);

    /// <summary>Visits an <see cref="HclBinaryExpression"/> node.</summary>
    void VisitBinary(HclBinaryExpression binary);

    /// <summary>Visits an <see cref="HclConditionalExpression"/> node.</summary>
    void VisitConditional(HclConditionalExpression conditional);

    /// <summary>Visits an <see cref="HclFunctionCallExpression"/> node.</summary>
    void VisitFunctionCall(HclFunctionCallExpression functionCall);

    /// <summary>Visits an <see cref="HclForExpression"/> node.</summary>
    void VisitFor(HclForExpression forExpr);

    /// <summary>Visits an <see cref="HclTupleExpression"/> node.</summary>
    void VisitTuple(HclTupleExpression tuple);

    /// <summary>Visits an <see cref="HclObjectExpression"/> node.</summary>
    void VisitObject(HclObjectExpression obj);
}
