using System.Text;
using TerraformDotnet.Hcl.Common;
using TerraformDotnet.Hcl.Exceptions;

namespace TerraformDotnet.Hcl.Reader;

public ref partial struct Utf8HclReader
{
    // Parsing context stack — tracks whether we're in body, expression, etc.
    // We use a simple state machine approach.

    /// <summary>
    /// Reads the next token from the HCL input.
    /// Returns <c>true</c> if a token was read, or <c>false</c> at end of input.
    /// </summary>
    public bool Read()
    {
        _hasValue = false;
        _valueSpan = default;

        // If we're in the middle of reading block labels, continue that
        if (_readingBlockLabels)
        {
            return ReadBlockLabel();
        }

        // When expecting a value (after '='), only skip spaces/tabs — preserve
        // newlines so ReadExpressionToken can detect end-of-expression.
        if (_expectingValue)
        {
            SkipWhitespace();

            if (IsAtEnd)
            {
                MarkTokenStart();
                _tokenType = HclTokenType.Eof;
                _expectingValue = false;
                _inBody = true;
                _expressionDepth = 0;
                return true;
            }

            MarkTokenStart();
            return ReadExpressionToken();
        }

        // Body-level: skip all trivia including newlines and comments
        SkipTrivia();

        // Check for comment tokens when ReadComments is enabled
        if (!IsAtEnd && _options.ReadComments && TryConsumeComment())
        {
            return true;
        }

        // Skip any remaining whitespace/newlines after potential comment skip
        SkipTrivia();

        if (IsAtEnd)
        {
            if (_tokenType != HclTokenType.Eof)
            {
                MarkTokenStart();
                _tokenType = HclTokenType.Eof;
                return true;
            }

            return false;
        }

        MarkTokenStart();

        // Body-level parsing: disambiguate identifiers as attributes vs blocks
        if (_inBody)
        {
            return ReadBodyElement();
        }

        // Expression context — should be handled by ReadExpressionToken
        return ReadExpressionToken();
    }

    /// <summary>
    /// Reads a body-level element: either an attribute or a block.
    /// </summary>
    private bool ReadBodyElement()
    {
        byte b = PeekByte();

        // Closing brace — end of block body
        if (b == (byte)'}')
        {
            AdvanceByte();
            _tokenType = HclTokenType.BlockEnd;
            _currentDepth--;
            return true;
        }

        // Must be an identifier (attribute name or block type)
        if (!IsIdStart(b) && !IsUtf8MultiByteStart(b))
        {
            throw new HclSyntaxException(
                $"Expected identifier at start of attribute or block definition, got '{(char)b}'.",
                new Mark(_tokenStart, _tokenStartLine, _tokenStartColumn));
        }

        ReadOnlySpan<byte> ident = ScanIdentifier();
        SkipWhitespace();

        if (IsAtEnd)
        {
            // Identifier at EOF — treat as identifier token
            _tokenType = HclTokenType.Identifier;
            _valueSpan = ident;
            return true;
        }

        byte next = PeekByte();

        // Attribute: identifier '=' expression
        if (next == (byte)'=')
        {
            // Check it's not '==' (equality operator)
            if (PeekByte(1) != (byte)'=')
            {
                _tokenType = HclTokenType.AttributeName;
                _valueSpan = ident;
                _expectingValue = true;
                AdvanceByte(); // consume '='
                return true;
            }
        }

        // Block: identifier (label)* '{'
        // The identifier is the block type
        _tokenType = HclTokenType.BlockType;
        _valueSpan = ident;

        // Read labels and opening brace in subsequent Read() calls
        _inBody = false;
        _readingBlockLabels = true;
        return true;
    }

    /// <summary>
    /// Reads block labels after a block type until we hit '{'.
    /// </summary>
    private bool ReadBlockLabel()
    {
        SkipWhitespace();

        if (IsAtEnd)
        {
            throw new HclSyntaxException(
                "Unexpected end of input: expected block body '{'.",
                new Mark(_position, _line, _column));
        }

        MarkTokenStart();
        byte b = PeekByte();

        // Opening brace — start of block body
        if (b == (byte)'{')
        {
            AdvanceByte();
            _tokenType = HclTokenType.BlockStart;
            _currentDepth++;
            _readingBlockLabels = false;

            if (_currentDepth > _options.MaxDepth)
            {
                throw new MaxRecursionDepthExceededException(
                    _options.MaxDepth,
                    _currentDepth,
                    new Mark(_tokenStart, _tokenStartLine, _tokenStartColumn));
            }

            _inBody = true;
            return true;
        }

        // String label
        if (b == (byte)'"')
        {
            ReadOnlySpan<byte> content = ScanQuotedString();
            _tokenType = HclTokenType.BlockLabel;
            _valueSpan = content;
            return true;
        }

        // Identifier label
        if (IsIdStart(b) || IsUtf8MultiByteStart(b))
        {
            ReadOnlySpan<byte> ident = ScanIdentifier();
            _tokenType = HclTokenType.BlockLabel;
            _valueSpan = ident;
            return true;
        }

        throw new HclSyntaxException(
            $"Expected block label or '{{', got '{(char)b}'.",
            new Mark(_tokenStart, _tokenStartLine, _tokenStartColumn));
    }

    /// <summary>
    /// Reads a token in expression context (attribute values, collection elements, etc.).
    /// </summary>
    private bool ReadExpressionToken()
    {
        SkipWhitespace();

        if (IsAtEnd)
        {
            MarkTokenStart();
            _tokenType = HclTokenType.Eof;
            _expectingValue = false;
            _inBody = true;
            _expressionDepth = 0;
            return true;
        }

        MarkTokenStart();
        byte b = PeekByte();

        // Newline in expression context with zero nesting depth means end of attribute value
        if (IsNewline(b) && _expressionDepth == 0)
        {
            _expectingValue = false;
            _inBody = true;
            ConsumeNewline();
            SkipTrivia();

            // Handle comments when transitioning back to body mode
            if (!IsAtEnd && _options.ReadComments && TryConsumeComment())
            {
                return true;
            }

            SkipTrivia();

            if (IsAtEnd)
            {
                _tokenType = HclTokenType.Eof;
                return true;
            }

            MarkTokenStart();
            return ReadBodyElement();
        }
        else if (IsNewline(b))
        {
            // Inside nested expression (parens, brackets, braces) — skip newlines
            SkipWhitespaceAndNewlines();
            if (IsAtEnd)
            {
                MarkTokenStart();
                _tokenType = HclTokenType.Eof;
                _expectingValue = false;
                _inBody = true;
                _expressionDepth = 0;
                return true;
            }

            MarkTokenStart();
            b = PeekByte();
        }

        // Skip comments within expressions
        if ((b == (byte)'#') ||
            (b == (byte)'/' && _position + 1 < _buffer.Length &&
             (PeekByte(1) == (byte)'/' || PeekByte(1) == (byte)'*')))
        {
            if (_options.ReadComments)
            {
                TryConsumeComment();
                return true;
            }

            TryConsumeComment();
            return ReadExpressionToken(); // recurse after skipping comment
        }

        // String literal
        if (b == (byte)'"')
        {
            ReadOnlySpan<byte> content = ScanQuotedString();
            _tokenType = HclTokenType.StringLiteral;
            _valueSpan = content;
            return true;
        }

        // Heredoc
        if (b == (byte)'<' && PeekByte(1) == (byte)'<')
        {
            ReadOnlySpan<byte> content = ScanHeredoc();
            _tokenType = HclTokenType.StringLiteral;
            _valueSpan = content;
            return true;
        }

        // Number literal
        if (IsDigit(b))
        {
            ReadOnlySpan<byte> number = ScanNumber();
            _tokenType = HclTokenType.NumberLiteral;
            _valueSpan = number;
            return true;
        }

        // Identifier, keyword, or function call
        if (IsIdStart(b) || IsUtf8MultiByteStart(b))
        {
            ReadOnlySpan<byte> ident = ScanIdentifier();
            SkipWhitespace();

            // Check for function call: identifier '('
            if (!IsAtEnd && PeekByte() == (byte)'(')
            {
                _tokenType = HclTokenType.FunctionCall;
                _valueSpan = ident;
                return true;
            }

            // Check for keywords
            if (ident.Length == 4 && ident[0] == (byte)'t' && ident[1] == (byte)'r' &&
                ident[2] == (byte)'u' && ident[3] == (byte)'e')
            {
                _tokenType = HclTokenType.BoolLiteral;
                _valueSpan = ident;
                return true;
            }

            if (ident.Length == 5 && ident[0] == (byte)'f' && ident[1] == (byte)'a' &&
                ident[2] == (byte)'l' && ident[3] == (byte)'s' && ident[4] == (byte)'e')
            {
                _tokenType = HclTokenType.BoolLiteral;
                _valueSpan = ident;
                return true;
            }

            if (ident.Length == 4 && ident[0] == (byte)'n' && ident[1] == (byte)'u' &&
                ident[2] == (byte)'l' && ident[3] == (byte)'l')
            {
                _tokenType = HclTokenType.NullLiteral;
                _valueSpan = ident;
                return true;
            }

            _tokenType = HclTokenType.Identifier;
            _valueSpan = ident;
            return true;
        }

        // Operators and delimiters
        HclTokenType opType = ScanOperatorOrDelimiter();
        if (opType != HclTokenType.None)
        {
            _tokenType = opType;

            // Track expression nesting depth
            switch (opType)
            {
                case HclTokenType.OpenParen:
                case HclTokenType.OpenBracket:
                case HclTokenType.OpenBrace:
                    _expressionDepth++;
                    break;
                case HclTokenType.CloseParen:
                case HclTokenType.CloseBracket:
                    _expressionDepth--;
                    break;
                case HclTokenType.CloseBrace:
                    _expressionDepth--;
                    if (_expressionDepth < 0)
                    {
                        // This closing brace ends the enclosing block, not an expression
                        _expressionDepth = 0;
                        _expectingValue = false;
                        _inBody = true;
                        _tokenType = HclTokenType.BlockEnd;
                        _currentDepth--;
                    }

                    break;
            }

            return true;
        }

        throw new HclSyntaxException(
            $"Unexpected character: '{(char)b}' (0x{b:X2}).",
            new Mark(_tokenStart, _tokenStartLine, _tokenStartColumn));
    }
}
