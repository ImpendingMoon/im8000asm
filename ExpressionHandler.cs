namespace im8000asm;

internal class ExpressionHandler
{
    private List<Token> _tokens = [];
    private int _position = 0;

    public Expression Parse(string text)
    {
        _tokens = Tokenize(text);
        Expression expression = ParseExpression();
        Expect(Constants.TokenType.End);
        return expression;
    }

    private Expression ParseExpression()
    {
        return ParseBitwise();
    }

    private Expression ParseBitwise()
    {
        Expression left = ParseAddSub();

        while (Match(Constants.TokenType.Ampersand, Constants.TokenType.Pipe, Constants.TokenType.Caret))
        {
            char operation = _tokens[_position - 1].Text[0];
            Expression right = ParseAddSub();
            left = new BinaryExpression(operation, left, right);
        }

        return left;
    }

    private Expression ParseAddSub()
    {
        Expression left = ParseMulDiv();

        while (Match(Constants.TokenType.Plus, Constants.TokenType.Minus))
        {
            char operation = _tokens[_position - 1].Text[0];
            Expression right = ParseMulDiv();
            left = new BinaryExpression(operation, left, right);
        }

        return left;
    }

    private Expression ParseMulDiv()
    {
        Expression left = ParseUnary();

        while (Match(Constants.TokenType.Star, Constants.TokenType.Slash))
        {
            char operation = _tokens[_position - 1].Text[0];
            Expression right = ParseUnary();
            left = new BinaryExpression(operation, left, right);
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (Match(Constants.TokenType.Plus, Constants.TokenType.Minus, Constants.TokenType.Tilde))
        {
            return new UnaryExpression(_tokens[_position - 1].Text, ParseUnary());
        }

        return ParsePrimary();
    }

    private Expression ParsePrimary()
    {
        if (Match(Constants.TokenType.Number))
        {
            // Special case for "$" (symbol to represent this instruction's base address)
            if (_tokens[_position - 1].Text == "$")
            {
                return new SymbolExpression(_tokens[_position - 1].Text);
            }

            if (!NumberParser.TryParseNumber(_tokens[_position - 1].Text, out int value))
            {
                throw new Exception($"Could not parse numeric literal {_tokens[_position - 1].Text}");
            }
            return new NumberExpression(value);
        }

        if (Match(Constants.TokenType.Identifier))
        {
            return new SymbolExpression(_tokens[_position - 1].Text);
        }

        if (Match(Constants.TokenType.LParen))
        {
            Expression expression = ParseExpression();
            Expect(Constants.TokenType.RParen);
            _position++;
            return expression;
        }

        throw new Exception("Expected expression");
    }

    private bool Match(params Constants.TokenType[] types)
    {
        foreach (Constants.TokenType type in types)
        {
            if (_tokens[_position].Type == type)
            {
                _position++;
                return true;
            }
        }
        return false;
    }

    private void Expect(Constants.TokenType type)
    {
        if (_tokens[_position].Type != type)
        {
            throw new Exception($"Expected {type} got {_tokens[_position].Type}");
        }
    }

    private static bool IsIdentifierChar(char c, bool start = false)
    {
        if (start)
        {
            return char.IsLetter(c) || c == '_' || c == '.';
        }

        return char.IsLetterOrDigit(c) || c == '_' || c == '.';
    }

    private static bool IsNumericChar(char c, bool start = false)
    {
        if (start)
        {
            return char.IsDigit(c) || c == '$' || c == '%';
        }

        // Valid binary, decimal, and hex. '_' as digit separator
        return char.IsDigit(c) || "abcdefABCDEF_xXbBhH".Contains(c);
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (IsIdentifierChar(c, start: true))
            {
                int start = i;
                i++;
                while (i < text.Length && IsIdentifierChar(text[i])) { i++; }

                tokens.Add(new Token(Constants.TokenType.Identifier, text[start..i]));
                continue;
            }

            if (IsNumericChar(c, start: true))
            {
                int start = i;
                i++;
                while (i < text.Length && IsNumericChar(text[i])) { i++; }

                tokens.Add(new Token(Constants.TokenType.Number, text[start..i]));
                continue;
            }

            tokens.Add(c switch
            {
                '+' => new Token(Constants.TokenType.Plus, "+"),
                '-' => new Token(Constants.TokenType.Minus, "-"),
                '*' => new Token(Constants.TokenType.Star, "*"),
                '/' => new Token(Constants.TokenType.Slash, "/"),
                '&' => new Token(Constants.TokenType.Ampersand, "&"),
                '|' => new Token(Constants.TokenType.Pipe, "|"),
                '^' => new Token(Constants.TokenType.Caret, "^"),
                '~' => new Token(Constants.TokenType.Tilde, "~"),
                '(' => new Token(Constants.TokenType.LParen, "("),
                ')' => new Token(Constants.TokenType.RParen, ")"),
                _ => throw new Exception($"Unexpected character '{c}' in expression")
            }
            );

            i++;
        }

        tokens.Add(new Token(Constants.TokenType.End, ""));
        return tokens;
    }

    public abstract record Expression();
    public record NumberExpression(int Value) : Expression;
    public record SymbolExpression(string Value) : Expression;
    public record UnaryExpression(string Operation, Expression Operand) : Expression;
    public record BinaryExpression(char Operation, Expression Left, Expression Right) : Expression;

    private record Token(Constants.TokenType Type, string Text);
}
