using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Tests.Reader;

public sealed class Utf8HclReaderOperatorTests
{
    private static Utf8HclReader CreateReader(string hcl)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(hcl);
        return new Utf8HclReader(bytes);
    }

    /// <summary>
    /// Reads "x = a {op} b" and returns the operator token type.
    /// </summary>
    private static HclTokenType ReadBinaryOperator(string op)
    {
        var reader = CreateReader($"x = a {op} b\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // Identifier 'a'
        Assert.True(reader.Read()); // Operator
        return reader.TokenType;
    }

    [Theory]
    [InlineData("+", HclTokenType.Plus)]
    [InlineData("-", HclTokenType.Minus)]
    [InlineData("*", HclTokenType.Star)]
    [InlineData("/", HclTokenType.Slash)]
    [InlineData("%", HclTokenType.Percent)]
    public void SingleCharArithmeticOperators(string op, HclTokenType expected)
    {
        Assert.Equal(expected, ReadBinaryOperator(op));
    }

    [Theory]
    [InlineData("==", HclTokenType.EqualEqual)]
    [InlineData("!=", HclTokenType.NotEqual)]
    [InlineData("<=", HclTokenType.LessEqual)]
    [InlineData(">=", HclTokenType.GreaterEqual)]
    [InlineData("&&", HclTokenType.And)]
    [InlineData("||", HclTokenType.Or)]
    public void MultiCharOperators(string op, HclTokenType expected)
    {
        Assert.Equal(expected, ReadBinaryOperator(op));
    }

    [Fact]
    public void LessThan()
    {
        Assert.Equal(HclTokenType.LessThan, ReadBinaryOperator("<"));
    }

    [Fact]
    public void GreaterThan()
    {
        Assert.Equal(HclTokenType.GreaterThan, ReadBinaryOperator(">"));
    }

    [Fact]
    public void QuestionMark()
    {
        var reader = CreateReader("x = a ? b : c\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // Identifier 'a'
        Assert.True(reader.Read()); // Question
        Assert.Equal(HclTokenType.Question, reader.TokenType);
    }

    [Fact]
    public void Colon()
    {
        var reader = CreateReader("x = a ? b : c\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // Identifier 'a'
        Assert.True(reader.Read()); // Question
        Assert.True(reader.Read()); // Identifier 'b'
        Assert.True(reader.Read()); // Colon
        Assert.Equal(HclTokenType.Colon, reader.TokenType);
    }

    [Fact]
    public void Comma()
    {
        var reader = CreateReader("x = [a, b]\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // OpenBracket
        Assert.True(reader.Read()); // Identifier 'a'
        Assert.True(reader.Read()); // Comma
        Assert.Equal(HclTokenType.Comma, reader.TokenType);
    }

    [Fact]
    public void Dot()
    {
        var reader = CreateReader("x = a.b\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // Identifier 'a'
        Assert.True(reader.Read()); // Dot
        Assert.Equal(HclTokenType.Dot, reader.TokenType);
    }

    [Fact]
    public void Ellipsis()
    {
        var reader = CreateReader("x = func(a...)\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // FunctionCall 'func'
        Assert.True(reader.Read()); // OpenParen
        Assert.True(reader.Read()); // Identifier 'a'
        Assert.True(reader.Read()); // Ellipsis
        Assert.Equal(HclTokenType.Ellipsis, reader.TokenType);
    }

    [Fact]
    public void FatArrow()
    {
        var reader = CreateReader("x = {for k, v in m : k => v}\n");
        // Skip to '=>'
        while (reader.Read() && reader.TokenType != HclTokenType.FatArrow)
        {
        }

        Assert.Equal(HclTokenType.FatArrow, reader.TokenType);
    }

    [Fact]
    public void Delimiters_BracesAndBrackets()
    {
        var reader = CreateReader("x = [{}]\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // OpenBracket
        Assert.Equal(HclTokenType.OpenBracket, reader.TokenType);
        Assert.True(reader.Read()); // OpenBrace
        Assert.Equal(HclTokenType.OpenBrace, reader.TokenType);
        Assert.True(reader.Read()); // CloseBrace
        Assert.Equal(HclTokenType.CloseBrace, reader.TokenType);
        Assert.True(reader.Read()); // CloseBracket
        Assert.Equal(HclTokenType.CloseBracket, reader.TokenType);
    }

    [Fact]
    public void Delimiters_Parentheses()
    {
        var reader = CreateReader("x = (a)\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // OpenParen
        Assert.Equal(HclTokenType.OpenParen, reader.TokenType);
        Assert.True(reader.Read()); // Identifier 'a'
        Assert.True(reader.Read()); // CloseParen
        Assert.Equal(HclTokenType.CloseParen, reader.TokenType);
    }

    [Fact]
    public void Not_VsNotEqual()
    {
        // ! alone is logical NOT
        var reader1 = CreateReader("x = !a\n");
        Assert.True(reader1.Read()); // AttributeName
        Assert.True(reader1.Read()); // Not
        Assert.Equal(HclTokenType.Not, reader1.TokenType);

        // != is not-equal
        var reader2 = CreateReader("x = a != b\n");
        Assert.True(reader2.Read()); // AttributeName
        Assert.True(reader2.Read()); // Identifier 'a'
        Assert.True(reader2.Read()); // NotEqual
        Assert.Equal(HclTokenType.NotEqual, reader2.TokenType);
    }

    [Fact]
    public void Equals_VsEqualEqual()
    {
        // = is attribute assignment
        var reader1 = CreateReader("x = 1\n");
        Assert.True(reader1.Read());
        Assert.Equal(HclTokenType.AttributeName, reader1.TokenType);

        // == inside expression is equality
        var reader2 = CreateReader("x = a == b\n");
        Assert.True(reader2.Read()); // AttributeName
        Assert.True(reader2.Read()); // Identifier 'a'
        Assert.True(reader2.Read()); // EqualEqual
        Assert.Equal(HclTokenType.EqualEqual, reader2.TokenType);
    }

    [Fact]
    public void DollarBrace_TemplateInterpolation()
    {
        var reader = CreateReader("x = \"${a}\"\n");
        Assert.True(reader.Read()); // AttributeName
        Assert.True(reader.Read()); // StringLiteral — template interpolation is in the raw value
        Assert.Equal(HclTokenType.StringLiteral, reader.TokenType);
    }

    [Fact]
    public void PercentBrace_PercentModulo()
    {
        // % alone is modulo
        Assert.Equal(HclTokenType.Percent, ReadBinaryOperator("%"));
    }
}
