using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;
using TerraformDotnet.Hcl.Reader;

namespace TerraformDotnet.Hcl.Nodes;

/// <summary>
/// Builds an <see cref="HclFile"/> AST from a <see cref="Utf8HclReader"/> token stream.
/// </summary>
public static class HclFileParser
{
    /// <summary>
    /// Parses UTF-8 HCL bytes into an AST.
    /// </summary>
    /// <param name="hclData">The UTF-8 encoded HCL source.</param>
    /// <param name="preserveComments">Whether to capture comments in the AST.</param>
    /// <returns>The parsed file AST.</returns>
    public static HclFile Parse(ReadOnlySpan<byte> hclData, bool preserveComments = true)
    {
        var options = new HclReaderOptions { ReadComments = preserveComments };
        var reader = new Utf8HclReader(hclData, options);
        var parser = new Parser(ref reader);
        return parser.ParseFile();
    }

    /// <summary>
    /// Recursive-descent parser with one-token lookahead.
    /// Invariant: after each parse method returns, <c>_current</c> holds the next
    /// unprocessed token. Methods inspect <c>_current</c> on entry and call
    /// <see cref="Advance"/> to consume it.
    /// </summary>
    private ref struct Parser
    {
        private Utf8HclReader _reader;
        private HclTokenType _current;
        private readonly List<HclComment> _pendingComments;

        public Parser(ref Utf8HclReader reader)
        {
            _reader = reader;
            _current = HclTokenType.None;
            _pendingComments = [];
        }

        private bool Advance()
        {
            bool result = _reader.Read();
            _current = _reader.TokenType;
            return result;
        }

        private string GetRawValue() =>
            _reader.ValueSpan.Length > 0 ? Encoding.UTF8.GetString(_reader.ValueSpan) : string.Empty;

        private string? GetStringValue() => _reader.GetString();

        private void SkipComments()
        {
            while (_current == HclTokenType.Comment)
            {
                _pendingComments.Add(CreateComment());
                Advance();
            }
        }

        // ── File & Body ─────────────────────────────────────────

        public HclFile ParseFile()
        {
            var file = new HclFile();
            Advance(); // prime with first token
            SkipComments();
            ParseBody(file.Body, isTopLevel: true);

            foreach (var c in _pendingComments)
            {
                file.DanglingComments.Add(c);
            }

            _pendingComments.Clear();
            file.End = _reader.Position;
            return file;
        }

        private void ParseBody(HclBody body, bool isTopLevel)
        {
            body.Start = _reader.TokenStart;
            var seenAttributes = new HashSet<string>();
            int previousEndLine = -1;

            while (true)
            {
                SkipComments();

                switch (_current)
                {
                    case HclTokenType.AttributeName:
                        var attr = ParseAttribute();
                        if (!seenAttributes.Add(attr.Name))
                        {
                            throw new HclSemanticException(
                                $"Duplicate attribute '{attr.Name}'.",
                                attr.Start);
                        }

                        if (previousEndLine >= 0)
                        {
                            int effectiveStart = attr.LeadingComments.Count > 0
                                ? attr.LeadingComments[0].Start.Line
                                : attr.Start.Line;
                            attr.HasLeadingBlankLine = effectiveStart - previousEndLine > 1;
                        }

                        previousEndLine = attr.End.Line;
                        body.Attributes.Add(attr);
                        break;

                    case HclTokenType.BlockType:
                        var block = ParseBlock();

                        if (previousEndLine >= 0)
                        {
                            int effectiveStart = block.LeadingComments.Count > 0
                                ? block.LeadingComments[0].Start.Line
                                : block.Start.Line;
                            block.HasLeadingBlankLine = effectiveStart - previousEndLine > 1;
                        }

                        previousEndLine = block.End.Line;
                        body.Blocks.Add(block);
                        break;

                    case HclTokenType.BlockEnd:
                        if (!isTopLevel)
                        {
                            body.End = _reader.Position;
                            Advance(); // consume BlockEnd
                            return;
                        }

                        Advance();
                        break;

                    case HclTokenType.Eof:
                        body.End = _reader.Position;
                        return;

                    default:
                        throw new HclSyntaxException(
                            $"Unexpected token {_current} in body context.",
                            _reader.TokenStart);
                }
            }
        }

        // ── Attribute ───────────────────────────────────────────

        private HclAttribute ParseAttribute()
        {
            Mark start = _reader.TokenStart;
            string name = GetRawValue();
            var leadingComments = DrainPendingComments();

            Advance(); // consume AttributeName → first expression token
            SkipComments();

            HclExpression value = ParseExpression(0);

            var attr = new HclAttribute
            {
                Name = name,
                Value = value,
                Start = start,
                End = value.End,
            };

            foreach (var c in leadingComments)
            {
                attr.LeadingComments.Add(c);
            }

            return attr;
        }

        // ── Block ───────────────────────────────────────────────

        private HclBlock ParseBlock()
        {
            Mark start = _reader.TokenStart;
            string type = GetRawValue();
            var leadingComments = DrainPendingComments();

            var block = new HclBlock { Type = type, Start = start };
            foreach (var c in leadingComments)
            {
                block.LeadingComments.Add(c);
            }

            Advance(); // consume BlockType

            while (_current == HclTokenType.BlockLabel)
            {
                block.Labels.Add(GetStringValue() ?? string.Empty);
                Advance();
            }

            if (_current != HclTokenType.BlockStart)
            {
                throw new HclSyntaxException(
                    $"Expected '{{' to start block body, got {_current}.",
                    _reader.TokenStart);
            }

            Advance(); // consume BlockStart
            ParseBody(block.Body, isTopLevel: false);
            // After ParseBody returns, _current is the token after BlockEnd
            block.End = _reader.Position;
            return block;
        }

        // ── Expression (Pratt / Precedence Climbing) ────────────

        private HclExpression ParseExpression(int minPrecedence)
        {
            var left = ParseUnary();

            while (true)
            {
                SkipComments();
                int prec = GetBinaryPrecedence(_current);
                if (prec < 0 || prec < minPrecedence)
                {
                    break;
                }

                if (_current == HclTokenType.Question)
                {
                    left = ParseConditional(left);
                    continue;
                }

                var op = TokenToBinaryOperator(_current);
                Advance(); // consume operator
                SkipComments();

                var right = ParseExpression(prec + 1);
                left = new HclBinaryExpression
                {
                    Left = left,
                    Operator = op,
                    Right = right,
                    Start = left.Start,
                    End = right.End,
                };
            }

            return left;
        }

        // ── Conditional ─────────────────────────────────────────

        private HclConditionalExpression ParseConditional(HclExpression condition)
        {
            Advance(); // consume ?
            SkipComments();

            var trueExpr = ParseExpression(0);

            if (_current != HclTokenType.Colon)
            {
                throw new HclSyntaxException(
                    "Expected ':' in conditional expression.",
                    _reader.TokenStart);
            }

            Advance(); // consume :
            SkipComments();

            var falseExpr = ParseExpression(0);

            return new HclConditionalExpression
            {
                Condition = condition,
                TrueResult = trueExpr,
                FalseResult = falseExpr,
                Start = condition.Start,
                End = falseExpr.End,
            };
        }

        // ── Unary ───────────────────────────────────────────────

        private HclExpression ParseUnary()
        {
            SkipComments();

            switch (_current)
            {
                case HclTokenType.Minus:
                {
                    Mark start = _reader.TokenStart;
                    Advance();
                    var operand = ParseUnary();
                    return new HclUnaryExpression
                    {
                        Operator = HclUnaryOperator.Negate,
                        Operand = operand,
                        Start = start,
                        End = operand.End,
                    };
                }

                case HclTokenType.Not:
                {
                    Mark start = _reader.TokenStart;
                    Advance();
                    var operand = ParseUnary();
                    return new HclUnaryExpression
                    {
                        Operator = HclUnaryOperator.Not,
                        Operand = operand,
                        Start = start,
                        End = operand.End,
                    };
                }

                default:
                    return ParsePostfix(ParsePrimary());
            }
        }

        // ── Primary ─────────────────────────────────────────────

        private HclExpression ParsePrimary()
        {
            Mark start = _reader.TokenStart;

            switch (_current)
            {
                case HclTokenType.NumberLiteral:
                {
                    var expr = new HclLiteralExpression
                    {
                        Value = GetRawValue(),
                        Kind = HclLiteralKind.Number,
                        Start = start,
                        End = _reader.Position,
                    };
                    Advance();
                    return expr;
                }

                case HclTokenType.BoolLiteral:
                {
                    var expr = new HclLiteralExpression
                    {
                        Value = GetRawValue(),
                        Kind = HclLiteralKind.Bool,
                        Start = start,
                        End = _reader.Position,
                    };
                    Advance();
                    return expr;
                }

                case HclTokenType.NullLiteral:
                {
                    var expr = new HclLiteralExpression
                    {
                        Value = null,
                        Kind = HclLiteralKind.Null,
                        Start = start,
                        End = _reader.Position,
                    };
                    Advance();
                    return expr;
                }

                case HclTokenType.StringLiteral:
                {
                    var expr = new HclLiteralExpression
                    {
                        Value = GetStringValue(),
                        Kind = HclLiteralKind.String,
                        Start = start,
                        End = _reader.Position,
                    };
                    Advance();
                    return expr;
                }

                case HclTokenType.Identifier:
                {
                    var expr = new HclVariableExpression
                    {
                        Name = GetRawValue(),
                        Start = start,
                        End = _reader.Position,
                    };
                    Advance();
                    return expr;
                }

                case HclTokenType.FunctionCall:
                    return ParseFunctionCall();

                case HclTokenType.OpenParen:
                    return ParseParenthesized();

                case HclTokenType.OpenBracket:
                    return ParseTupleOrFor();

                case HclTokenType.OpenBrace:
                    return ParseObjectOrFor();

                default:
                    throw new HclSyntaxException(
                        $"Expected expression, got {_current}.",
                        start);
            }
        }

        // ── Postfix (Dot, Index, Splat) ─────────────────────────

        private HclExpression ParsePostfix(HclExpression expr)
        {
            while (true)
            {
                switch (_current)
                {
                    case HclTokenType.Dot:
                        expr = ParseDotAccess(expr);
                        break;

                    case HclTokenType.OpenBracket:
                        expr = ParseIndexAccess(expr);
                        break;

                    default:
                        return expr;
                }
            }
        }

        private HclExpression ParseDotAccess(HclExpression source)
        {
            Advance(); // consume Dot

            if (_current == HclTokenType.Star)
            {
                Mark end = _reader.Position;
                Advance(); // consume *
                var splat = new HclSplatExpression
                {
                    Source = source,
                    IsFullSplat = false,
                    Start = source.Start,
                    End = end,
                };
                return ParseSplatTraversal(splat);
            }

            if (_current == HclTokenType.NumberLiteral)
            {
                var index = new HclLiteralExpression
                {
                    Value = GetRawValue(),
                    Kind = HclLiteralKind.Number,
                    Start = _reader.TokenStart,
                    End = _reader.Position,
                };
                Mark end = _reader.Position;
                Advance();
                return new HclIndexExpression
                {
                    Collection = source,
                    Index = index,
                    IsLegacy = true,
                    Start = source.Start,
                    End = end,
                };
            }

            if (_current == HclTokenType.Identifier)
            {
                string name = GetRawValue();
                Mark end = _reader.Position;
                Advance();
                return new HclAttributeAccessExpression
                {
                    Source = source,
                    Name = name,
                    Start = source.Start,
                    End = end,
                };
            }

            throw new HclSyntaxException(
                $"Expected identifier, number, or '*' after '.', got {_current}.",
                _reader.TokenStart);
        }

        private HclExpression ParseIndexAccess(HclExpression source)
        {
            Advance(); // consume [

            if (_current == HclTokenType.Star)
            {
                Advance(); // consume *
                if (_current != HclTokenType.CloseBracket)
                {
                    throw new HclSyntaxException("Expected ']' after '[*'.", _reader.TokenStart);
                }

                Mark end = _reader.Position;
                Advance(); // consume ]
                var splat = new HclSplatExpression
                {
                    Source = source,
                    IsFullSplat = true,
                    Start = source.Start,
                    End = end,
                };
                return ParseSplatTraversal(splat);
            }

            SkipComments();
            var index = ParseExpression(0);

            if (_current != HclTokenType.CloseBracket)
            {
                throw new HclSyntaxException("Expected ']'.", _reader.TokenStart);
            }

            Mark endPos = _reader.Position;
            Advance(); // consume ]
            return new HclIndexExpression
            {
                Collection = source,
                Index = index,
                Start = source.Start,
                End = endPos,
            };
        }

        private HclExpression ParseSplatTraversal(HclSplatExpression splat)
        {
            while (true)
            {
                if (_current == HclTokenType.Dot)
                {
                    Advance();
                    if (_current != HclTokenType.Identifier)
                    {
                        throw new HclSyntaxException(
                            $"Expected identifier after '.' in splat traversal, got {_current}.",
                            _reader.TokenStart);
                    }

                    splat.Traversal.Add(new HclAttributeAccessExpression
                    {
                        Source = new HclVariableExpression
                        {
                            Name = "*",
                            Start = default,
                            End = default,
                        },
                        Name = GetRawValue(),
                        Start = _reader.TokenStart,
                        End = _reader.Position,
                    });
                    splat.End = _reader.Position;
                    Advance();
                }
                else if (_current == HclTokenType.OpenBracket)
                {
                    Advance(); // consume [
                    SkipComments();
                    var indexExpr = ParseExpression(0);

                    if (_current != HclTokenType.CloseBracket)
                    {
                        throw new HclSyntaxException(
                            "Expected ']' in splat traversal.",
                            _reader.TokenStart);
                    }

                    splat.Traversal.Add(new HclIndexExpression
                    {
                        Collection = new HclVariableExpression
                        {
                            Name = "*",
                            Start = default,
                            End = default,
                        },
                        Index = indexExpr,
                        Start = _reader.TokenStart,
                        End = _reader.Position,
                    });
                    splat.End = _reader.Position;
                    Advance(); // consume ]
                }
                else
                {
                    return splat;
                }
            }
        }

        // ── Function Call ───────────────────────────────────────

        private HclFunctionCallExpression ParseFunctionCall()
        {
            Mark start = _reader.TokenStart;
            string name = GetRawValue();
            Advance(); // consume FunctionCall token

            if (_current != HclTokenType.OpenParen)
            {
                throw new HclSyntaxException(
                    "Expected '(' after function name.",
                    _reader.TokenStart);
            }

            Advance(); // consume (
            SkipComments();

            var funcCall = new HclFunctionCallExpression { Name = name, Start = start };

            if (_current == HclTokenType.CloseParen)
            {
                funcCall.End = _reader.Position;
                Advance(); // consume )
                return funcCall;
            }

            // First argument
            funcCall.Arguments.Add(ParseExpression(0));

            if (_current == HclTokenType.Ellipsis)
            {
                funcCall.ExpandFinalArgument = true;
                Advance();
            }

            while (_current == HclTokenType.Comma)
            {
                Advance(); // consume ,
                SkipComments();
                if (_current == HclTokenType.CloseParen)
                {
                    break; // trailing comma
                }

                funcCall.Arguments.Add(ParseExpression(0));

                if (_current == HclTokenType.Ellipsis)
                {
                    funcCall.ExpandFinalArgument = true;
                    Advance();
                }
            }

            if (_current != HclTokenType.CloseParen)
            {
                throw new HclSyntaxException(
                    "Expected ')' to close function call.",
                    _reader.TokenStart);
            }

            funcCall.End = _reader.Position;
            Advance(); // consume )
            return funcCall;
        }

        // ── Parenthesized ───────────────────────────────────────

        private HclExpression ParseParenthesized()
        {
            Advance(); // consume (
            SkipComments();

            var inner = ParseExpression(0);

            if (_current != HclTokenType.CloseParen)
            {
                throw new HclSyntaxException("Expected ')'.", _reader.TokenStart);
            }

            Advance(); // consume )
            return inner;
        }

        // ── Tuple / For ─────────────────────────────────────────

        private HclExpression ParseTupleOrFor()
        {
            Mark start = _reader.TokenStart;
            Advance(); // consume [
            SkipComments();

            if (_current == HclTokenType.CloseBracket)
            {
                Mark end = _reader.Position;
                Advance();
                return new HclTupleExpression { Start = start, End = end };
            }

            if (_current == HclTokenType.Identifier && GetRawValue() == "for")
            {
                return ParseForExpression(start, isObject: false);
            }

            var tuple = new HclTupleExpression { Start = start };
            tuple.Elements.Add(ParseExpression(0));

            while (_current == HclTokenType.Comma)
            {
                Advance(); // consume ,
                SkipComments();
                if (_current == HclTokenType.CloseBracket)
                {
                    break; // trailing comma
                }

                tuple.Elements.Add(ParseExpression(0));
            }

            if (_current != HclTokenType.CloseBracket)
            {
                throw new HclSyntaxException("Expected ']'.", _reader.TokenStart);
            }

            tuple.End = _reader.Position;
            Advance();
            return tuple;
        }

        // ── Object / For ────────────────────────────────────────

        private HclExpression ParseObjectOrFor()
        {
            Mark start = _reader.TokenStart;
            Advance(); // consume {
            SkipComments();

            if (_current == HclTokenType.CloseBrace)
            {
                Mark end = _reader.Position;
                Advance();
                return new HclObjectExpression { Start = start, End = end };
            }

            if (_current == HclTokenType.Identifier && GetRawValue() == "for")
            {
                return ParseForExpression(start, isObject: true);
            }

            var obj = new HclObjectExpression { Start = start };
            obj.Elements.Add(ParseObjectElement());

            // Objects support both comma and newline separation.
            // The reader skips newlines inside braces, so consecutive
            // elements appear without an explicit separator token.
            while (true)
            {
                SkipComments();
                if (_current == HclTokenType.CloseBrace || _current == HclTokenType.Eof)
                {
                    break;
                }

                if (_current == HclTokenType.Comma)
                {
                    Advance();
                    SkipComments();
                    if (_current == HclTokenType.CloseBrace)
                    {
                        break; // trailing comma
                    }
                }

                obj.Elements.Add(ParseObjectElement());
            }

            if (_current != HclTokenType.CloseBrace)
            {
                throw new HclSyntaxException("Expected '}'.", _reader.TokenStart);
            }

            obj.End = _reader.Position;
            Advance();
            return obj;
        }

        private HclObjectElement ParseObjectElement()
        {
            bool forceKey = false;
            HclExpression key;

            if (_current == HclTokenType.OpenParen)
            {
                forceKey = true;
                key = ParseParenthesized(); // handles ( expr )
            }
            else
            {
                key = ParsePrimary(); // identifier, string, number
            }

            bool usesColon = _current == HclTokenType.Colon;
            if (_current != HclTokenType.Equals && _current != HclTokenType.Colon)
            {
                throw new HclSyntaxException(
                    $"Expected '=' or ':' after object key, got {_current}.",
                    _reader.TokenStart);
            }

            Advance(); // consume = or :
            SkipComments();

            var value = ParseExpression(0);

            return new HclObjectElement
            {
                Key = key,
                Value = value,
                ForceKey = forceKey,
                UsesColon = usesColon,
            };
        }

        // ── For Expression ──────────────────────────────────────

        private HclForExpression ParseForExpression(Mark start, bool isObject)
        {
            Advance(); // consume 'for' identifier

            if (_current != HclTokenType.Identifier)
            {
                throw new HclSyntaxException(
                    "Expected variable name after 'for'.",
                    _reader.TokenStart);
            }

            string firstVar = GetRawValue();
            Advance();

            string? secondVar = null;
            if (_current == HclTokenType.Comma)
            {
                Advance(); // consume ,
                if (_current != HclTokenType.Identifier)
                {
                    throw new HclSyntaxException(
                        "Expected variable name after ','.",
                        _reader.TokenStart);
                }

                secondVar = GetRawValue();
                Advance();
            }

            if (_current != HclTokenType.Identifier || GetRawValue() != "in")
            {
                throw new HclSyntaxException(
                    "Expected 'in' keyword in for expression.",
                    _reader.TokenStart);
            }

            Advance(); // consume 'in'
            SkipComments();

            var collection = ParseExpression(0);

            if (_current != HclTokenType.Colon)
            {
                throw new HclSyntaxException(
                    "Expected ':' in for expression.",
                    _reader.TokenStart);
            }

            Advance(); // consume :
            SkipComments();

            var resultExpr = ParseExpression(0);
            HclExpression? keyExpr = null;
            bool isGrouped = false;
            HclExpression? condition = null;

            if (isObject && _current == HclTokenType.FatArrow)
            {
                keyExpr = resultExpr;
                Advance(); // consume =>
                SkipComments();
                resultExpr = ParseExpression(0);

                if (_current == HclTokenType.Ellipsis)
                {
                    isGrouped = true;
                    Advance();
                }
            }

            if (_current == HclTokenType.Identifier && GetRawValue() == "if")
            {
                Advance(); // consume 'if'
                condition = ParseExpression(0);
            }

            var expectedClose = isObject
                ? HclTokenType.CloseBrace
                : HclTokenType.CloseBracket;

            if (_current != expectedClose)
            {
                throw new HclSyntaxException(
                    $"Expected '{(isObject ? "}" : "]")}' to close for expression.",
                    _reader.TokenStart);
            }

            Mark end = _reader.Position;
            Advance(); // consume closing delimiter

            return new HclForExpression
            {
                KeyVariable = firstVar,
                ValueVariable = secondVar,
                Collection = collection,
                ValueExpression = resultExpr,
                KeyExpression = keyExpr,
                Condition = condition,
                IsObjectFor = isObject && keyExpr is not null,
                IsGrouped = isGrouped,
                Start = start,
                End = end,
            };
        }

        // ── Helpers ─────────────────────────────────────────────

        private static int GetBinaryPrecedence(HclTokenType token) => token switch
        {
            HclTokenType.Question => 0,
            HclTokenType.Or => 1,
            HclTokenType.And => 2,
            HclTokenType.EqualEqual or HclTokenType.NotEqual => 3,
            HclTokenType.LessThan or HclTokenType.GreaterThan or
                HclTokenType.LessEqual or HclTokenType.GreaterEqual => 4,
            HclTokenType.Plus or HclTokenType.Minus => 5,
            HclTokenType.Star or HclTokenType.Slash or HclTokenType.Percent => 6,
            _ => -1,
        };

        private static HclBinaryOperator TokenToBinaryOperator(HclTokenType token) => token switch
        {
            HclTokenType.Plus => HclBinaryOperator.Add,
            HclTokenType.Minus => HclBinaryOperator.Subtract,
            HclTokenType.Star => HclBinaryOperator.Multiply,
            HclTokenType.Slash => HclBinaryOperator.Divide,
            HclTokenType.Percent => HclBinaryOperator.Modulo,
            HclTokenType.EqualEqual => HclBinaryOperator.Equal,
            HclTokenType.NotEqual => HclBinaryOperator.NotEqual,
            HclTokenType.LessThan => HclBinaryOperator.LessThan,
            HclTokenType.GreaterThan => HclBinaryOperator.GreaterThan,
            HclTokenType.LessEqual => HclBinaryOperator.LessEqual,
            HclTokenType.GreaterEqual => HclBinaryOperator.GreaterEqual,
            HclTokenType.And => HclBinaryOperator.And,
            HclTokenType.Or => HclBinaryOperator.Or,
            _ => throw new InvalidOperationException($"Not a binary operator: {token}"),
        };

        private HclComment CreateComment()
        {
            string text = GetRawValue();
            HclCommentStyle style = _reader.CommentMarker switch
            {
                (byte)'#' => HclCommentStyle.Hash,
                (byte)'*' => HclCommentStyle.Block,
                _ => HclCommentStyle.Line,
            };

            return new HclComment
            {
                Text = text,
                Style = style,
                Start = _reader.TokenStart,
                End = _reader.Position,
            };
        }

        private List<HclComment> DrainPendingComments()
        {
            if (_pendingComments.Count == 0)
            {
                return [];
            }

            var result = new List<HclComment>(_pendingComments);
            _pendingComments.Clear();
            return result;
        }
    }
}
