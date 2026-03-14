namespace im8000asm;

public enum TokenKind
{
	Identifier,
	Number,
	StringLiteral,
	Comma,
	Colon,
	LeftParen,
	RightParen,
	LeftBracket,
	RightBracket,
	Plus,
	Minus,
	Star,
	Slash,
	Percent,
	ShiftLeft,
	ShiftRight,
	Tilde,
	Ampersand,
	Caret,
	Pipe,
	Dollar,
	NewLine,
	EndOfFile,
}

public record Token(TokenKind Kind, string Text, ulong NumericValue, int Line, int Column);

public class Lexer
{
	private readonly string _source;
	private int _column = 1;
	private int _line = 1;
	private int _position;

	public Lexer(string[] sourceLines)
	{
		_source = string.Join('\n', sourceLines);
	}

	public List<Token> Tokenize()
	{
		List<Token> tokens = [];
		Token token;
		do
		{
			token = NextToken();
			tokens.Add(token);
		} while (token.Kind != TokenKind.EndOfFile);
		return tokens;
	}

	private Token NextToken()
	{
		SkipWhitespaceAndComments();

		if (_position >= _source.Length)
		{
			return new Token(TokenKind.EndOfFile, string.Empty, 0, _line, _column);
		}

		int startLine = _line, startColumn = _column;
		char current = CurrentChar();

		// Newline
		if (current is '\n' or '\r')
		{
			ConsumeNewLine();
			return new Token(TokenKind.NewLine, "\\n", 0, startLine, startColumn);
		}

		// Multi-char operators
		if (current == '<' && Peek(1) == '<')
		{
			Advance(2);
			return new Token(TokenKind.ShiftLeft, "<<", 0, startLine, startColumn);
		}
		if (current == '>' && Peek(1) == '>')
		{
			Advance(2);
			return new Token(TokenKind.ShiftRight, ">>", 0, startLine, startColumn);
		}

		// Single-char punctuation
		if (TryMatchPunctuation(current, out TokenKind kind))
		{
			Advance();
			return new Token(kind, current.ToString(), 0, startLine, startColumn);
		}

		// Numbers
		if (current == '$' || current == '%' || char.IsDigit(current))
		{
			return ReadNumber(startLine, startColumn);
		}

		// Identifiers
		if (char.IsLetter(current) || current == '_' || current == '.')
		{
			return ReadIdentifier(startLine, startColumn);
		}

		// String literals
		if (current == '"')
		{
			return ReadString(startLine, startColumn);
		}

		// Unknown: fallback as identifier
		Advance();
		return new Token(TokenKind.Identifier, current.ToString(), 0, startLine, startColumn);
	}

	private Token ReadIdentifier(int startLine, int startColumn)
	{
		int start = _position;

		// Consume optional leading '.' (local label prefix)
		if (CurrentChar() == '.')
		{
			Advance();
		}

		// Consume the base name segment
		while (_position < _source.Length && (char.IsLetterOrDigit(CurrentChar()) || CurrentChar() == '_'))
		{
			Advance();
		}

		// Consume additional '.label' chunks to support qualified names
		while (_position < _source.Length && CurrentChar() == '.')
		{
			int nextPos = _position + 1;
			if (nextPos >= _source.Length)
			{
				break;
			}

			char firstOfSegment = _source[nextPos];
			if (!char.IsLetter(firstOfSegment) && firstOfSegment != '_')
			{
				break; // not an identifier segment
			}

			// Measure the full segment length
			int segStart = nextPos;
			int segEnd = segStart;
			while (segEnd < _source.Length && (char.IsLetterOrDigit(_source[segEnd]) || _source[segEnd] == '_'))
			{
				segEnd++;
			}

			string segment = _source[segStart..segEnd];

			// If the segment is exactly a size suffix identifier and is followed by a
			// non-identifier character, treat it as the size suffix and stop.
			bool isSizeSuffix = segment.Length == 1 &&
				"bwdBWD".Contains(segment[0]) &&
				(segEnd >= _source.Length || (!char.IsLetterOrDigit(_source[segEnd]) && _source[segEnd] != '_'));

			if (isSizeSuffix)
			{
				// Consume the dot + size letter as the size suffix and stop.
				Advance(1 + segment.Length);
				break;
			}

			// Otherwise consume the dot + segment as part of the qualified name.
			Advance(1 + segment.Length);
		}

		// alternate-register suffix: '
		if (_position < _source.Length && CurrentChar() == '\'')
		{
			Advance();
		}

		return new Token(TokenKind.Identifier, _source[start.._position], 0, startLine, startColumn);
	}

	private Token ReadNumber(int startLine, int startColumn)
	{
		int start = _position;

		// Consume all number characters
		while (_position < _source.Length &&
			(char.IsLetterOrDigit(CurrentChar()) || CurrentChar() is '_' or '$' or '%' or '.'))
		{
			Advance();
		}

		string raw = _source[start.._position].Replace("_", ""); // remove underscores
		string numberPart = raw;
		int numberBase = 10;

		// Prefixes
		if (numberPart.StartsWith('$'))
		{
			numberBase = 16;
			numberPart = numberPart[1..];
		}
		else if (numberPart.StartsWith('%'))
		{
			numberBase = 2;
			numberPart = numberPart[1..];
		}
		else if (numberPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			numberBase = 16;
			numberPart = numberPart[2..];
		}
		else if (numberPart.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
		{
			numberBase = 2;
			numberPart = numberPart[2..];
		}

		// Suffixes
		if (numberPart.EndsWith("h", StringComparison.OrdinalIgnoreCase))
		{
			numberBase = 16;
			numberPart = numberPart[..^1];
		}
		else if (numberPart.EndsWith("b", StringComparison.OrdinalIgnoreCase))
		{
			numberBase = 2;
			numberPart = numberPart[..^1];
		}

		ulong value = numberPart.Length > 0 ? Convert.ToUInt64(numberPart, numberBase) : 0;

		return new Token(TokenKind.Number, raw, value, startLine, startColumn);
	}

	private Token ReadString(int startLine, int startColumn)
	{
		Advance(); // skip opening quote
		int start = _position;
		while (_position < _source.Length && CurrentChar() != '"' && CurrentChar() != '\n')
		{
			Advance();
		}
		string content = _source[start.._position];
		if (_position < _source.Length && CurrentChar() == '"')
		{
			Advance(); // skip closing quote
		}
		return new Token(TokenKind.StringLiteral, content, 0, startLine, startColumn);
	}

	private void SkipWhitespaceAndComments()
	{
		while (_position < _source.Length)
		{
			char c = CurrentChar();
			if (c is ' ' or '\t')
			{
				Advance();
				continue;
			}
			if (c == ';')
			{
				SkipToEndOfLine();
				continue;
			}
			break;
		}
	}

	private void SkipToEndOfLine()
	{
		while (_position < _source.Length && CurrentChar() != '\n' && CurrentChar() != '\r')
		{
			Advance();
		}
	}

	private void ConsumeNewLine()
	{
		if (CurrentChar() == '\r')
		{
			Advance();
		}
		if (_position < _source.Length && CurrentChar() == '\n')
		{
			Advance();
		}
		_line++;
		_column = 1;
	}

	private char CurrentChar()
	{
		return _source[_position];
	}

	private char Peek(int offset)
	{
		return (_position + offset) < _source.Length ? _source[_position + offset] : '\0';
	}

	private void Advance(int count = 1)
	{
		_position += count;
		_column += count;
	}

	private static bool TryMatchPunctuation(char c, out TokenKind kind)
	{
		kind = c switch
		{
			',' => TokenKind.Comma,
			':' => TokenKind.Colon,
			'(' => TokenKind.LeftParen,
			')' => TokenKind.RightParen,
			'+' => TokenKind.Plus,
			'-' => TokenKind.Minus,
			'*' => TokenKind.Star,
			'/' => TokenKind.Slash,
			'&' => TokenKind.Ampersand,
			'^' => TokenKind.Caret,
			'|' => TokenKind.Pipe,
			'~' => TokenKind.Tilde,
			'[' => TokenKind.LeftBracket,
			']' => TokenKind.RightBracket,
			_ => (TokenKind)(-1),
		};
		return kind != (TokenKind)(-1);
	}
}
