using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

/// <summary>
/// Tests template expression tokenization (strings with interpolation/directives).
/// At the reader level, template strings are emitted as single StringLiteral tokens
/// containing the raw template content. Template parsing is handled at the AST level.
/// </summary>
public sealed class Utf8HclReaderExpressionTemplateTests
{
    private static List<(HclTokenType Type, string Value)> ExprTokens(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        var reader = new Utf8HclReader(bytes);
        var tokens = new List<(HclTokenType, string)>();
        while (reader.Read())
        {
            string value = reader.ValueSpan.Length > 0
                ? Encoding.UTF8.GetString(reader.ValueSpan)
                : "";
            if (reader.TokenType != HclTokenType.AttributeName && reader.TokenType != HclTokenType.Eof)
            {
                tokens.Add((reader.TokenType, value));
            }
        }

        return tokens;
    }

    [Fact]
    public void SimpleInterpolation()
    {
        var expr = ExprTokens("x = \"Hello, ${var.name}!\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Contains("${var.name}", expr[0].Value);
    }

    [Fact]
    public void MultipleInterpolations()
    {
        var expr = ExprTokens("x = \"${var.a}-${var.b}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Contains("${var.a}", expr[0].Value);
        Assert.Contains("${var.b}", expr[0].Value);
    }

    [Fact]
    public void InterpolationWithExpression()
    {
        var expr = ExprTokens("x = \"count: ${length(var.list)}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Contains("${length(var.list)}", expr[0].Value);
    }

    [Fact]
    public void IfDirective()
    {
        var expr = ExprTokens("x = \"%{if var.x}yes%{endif}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Contains("%{if var.x}", expr[0].Value);
    }

    [Fact]
    public void IfElseDirective()
    {
        var expr = ExprTokens("x = \"%{if var.x}yes%{else}no%{endif}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        string val = expr[0].Value;
        Assert.Contains("%{if var.x}", val);
        Assert.Contains("%{else}", val);
        Assert.Contains("%{endif}", val);
    }

    [Fact]
    public void ForDirective()
    {
        var expr = ExprTokens("x = \"%{for v in var.list}${v}\\n%{endfor}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        string val = expr[0].Value;
        Assert.Contains("%{for v in var.list}", val);
        Assert.Contains("%{endfor}", val);
    }

    [Fact]
    public void EscapedInterpolation()
    {
        // $${literal} → the reader should include the raw $$ in the span
        var expr = ExprTokens("x = \"$${literal}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        // The raw token should contain $$ (escaping is for template evaluation, not tokenization)
        Assert.Single(expr);
    }

    [Fact]
    public void EscapedDirective()
    {
        var expr = ExprTokens("x = \"%%{literal}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Single(expr);
    }

    [Fact]
    public void StringWithoutInterpolation()
    {
        var expr = ExprTokens("x = \"plain string\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Equal("plain string", expr[0].Value);
    }

    [Fact]
    public void HeredocWithInterpolation()
    {
        var expr = ExprTokens("x = <<EOF\n${greeting}\nEOF\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Contains("${greeting}", expr[0].Value);
    }

    [Fact]
    public void InterpolationFollowedByAnotherAttribute()
    {
        string hcl = "a = \"${x}\"\nb = 2\n";
        var all = ExprTokens(hcl);
        // Should have: StringLiteral for "${x}", then the 'b' attribute chain
        Assert.Equal(HclTokenType.StringLiteral, all[0].Type);
    }

    [Fact]
    public void EmptyString()
    {
        var expr = ExprTokens("x = \"\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Equal("", expr[0].Value);
    }

    [Fact]
    public void StringWithNestedQuotesInInterpolation()
    {
        // "${func("inner")}" — nested quotes within interpolation
        var expr = ExprTokens("x = \"${func(\"inner\")}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
    }

    [Fact]
    public void InterpolationOnlyString()
    {
        // "${var.value}" — unwrap-eligible (single interpolation, nothing else)
        var expr = ExprTokens("x = \"${var.value}\"\n");
        Assert.Equal(HclTokenType.StringLiteral, expr[0].Type);
        Assert.Contains("${var.value}", expr[0].Value);
    }
}
