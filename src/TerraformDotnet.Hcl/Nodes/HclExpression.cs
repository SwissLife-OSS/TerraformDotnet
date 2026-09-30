using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;

namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Abstract base class for all HCL expression nodes.
/// </summary>
public abstract class HclExpression : HclNode
{
    /// <summary>Attribute prefix used to parse a standalone expression through the file parser.</summary>
    private const string ExpressionPrefix = "__expression__ = ";

    /// <inheritdoc />
    public override HclNodeType NodeType => HclNodeType.Expression;

    /// <summary>
    /// Parses a standalone HCL expression such as <c>contains(["a", "b"], var.x)</c>.
    /// </summary>
    /// <param name="expression">The expression source text.</param>
    /// <returns>The parsed expression AST.</returns>
    /// <exception cref="HclException">Thrown when the text is not a single valid expression.</exception>
    /// <remarks>
    /// Source positions in the returned tree are relative to the expression text, shifted by the
    /// length of an internal prefix on the first line.
    /// </remarks>
    /// <example>
    /// <code>
    /// var expr = HclExpression.Parse("length(var.name) &lt;= 63");
    /// </code>
    /// </example>
    public static HclExpression Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var file = HclFileParser.Parse(Encoding.UTF8.GetBytes(ExpressionPrefix + expression), preserveComments: false);
        if (file.Body.Attributes.Count != 1 || file.Body.Blocks.Count != 0)
        {
            throw new HclSyntaxException(
                "Expected a single HCL expression.",
                new Mark(0, 1, 1));
        }

        return file.Body.Attributes[0].Value;
    }

    /// <summary>
    /// Attempts to parse a standalone HCL expression.
    /// </summary>
    /// <param name="expression">The expression source text.</param>
    /// <param name="result">When this method returns <c>true</c>, the parsed expression.</param>
    /// <returns><c>true</c> if the text is a single valid expression; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? expression, out HclExpression? result)
    {
        result = null;
        if (expression is null)
        {
            return false;
        }

        try
        {
            result = Parse(expression);

            return true;
        }
        catch (HclException)
        {
            return false;
        }
    }
}
