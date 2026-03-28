using System.Text;

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
		if (char.IsDigit(current))
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

		// Character literals
		if (current == '\'')
		{
			return ReadCharLiteral(startLine, startColumn);
		}

		Advance();
		return new Token(TokenKind.Identifier, current.ToString(), 0, startLine, startColumn);
	}

	private Token ReadIdentifier(int startLine, int startColumn)
	{
		int start = _position;

		while (_position < _source.Length && (char.IsLetterOrDigit(CurrentChar()) || CurrentChar() is '_' or '.'))
		{
			Advance();
		}

		// Alternate register suffix
		if (_position < _source.Length && CurrentChar() == '\'')
		{
			Advance();
		}

		return new Token(TokenKind.Identifier, _source[start.._position], 0, startLine, startColumn);
	}

	private Token ReadNumber(int startLine, int startColumn)
	{
		int start = _position;

		while (_position < _source.Length && (char.IsLetterOrDigit(CurrentChar()) || CurrentChar() == '_'))
		{
			Advance();
		}

		string original = _source[start.._position];
		string raw = original.Replace("_", "");
		string numberPart = raw;
		int numberBase = 10;

		if (numberPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			numberBase = 16;
			numberPart = numberPart[2..];
		}
		else if (numberPart.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
		{
			numberBase = 2;
			numberPart = numberPart[2..];
		}

		if (numberPart.Length == 0)
		{
			throw new AssemblyException(startLine, startColumn, "Expected value in number literal");
		}

		ulong value;
		try
		{
			value = Convert.ToUInt64(numberPart, numberBase);
		}
		catch (Exception ex) when (ex is OverflowException or FormatException)
		{
			throw new AssemblyException(startLine, startColumn, $"Invalid number literal '{original}': {ex.Message}");
		}

		return new Token(TokenKind.Number, original, value, startLine, startColumn);
	}

	private Token ReadCharLiteral(int startLine, int startColumn)
	{
		Advance(); // skip opening single quote

		if (_position >= _source.Length || CurrentChar() is '\n' or '\r')
		{
			throw new AssemblyException(startLine, startColumn, "Unterminated character literal");
		}

		ulong value;
		string rawText;

		if (CurrentChar() == '\\')
		{
			int escStart = _position;
			Advance(); // skip backslash
			value = ReadEscapeSequence(startLine, startColumn);
			rawText = _source[escStart.._position];
		}
		else
		{
			rawText = CurrentChar().ToString();
			value = CurrentChar();
			Advance();
		}

		if (_position >= _source.Length || CurrentChar() != '\'')
		{
			throw new AssemblyException(startLine, startColumn, "Expected closing \"'\" after character literal");
		}
		Advance(); // skip closing single quote

		return new Token(TokenKind.Number, $"'{rawText}'", value, startLine, startColumn);
	}

	private ulong ReadEscapeSequence(int startLine, int startColumn)
	{
		if (_position >= _source.Length)
		{
			throw new AssemblyException(startLine, startColumn, "Unterminated escape sequence");
		}

		char code = CurrentChar();
		Advance();

		switch (code)
		{
			case '\\': return '\\';
			case '\'': return '\'';
			case '"': return '"';
			case '0': return '\0';
			case 'n': return '\n';
			case 'r': return '\r';
			case 't': return '\t';

			case 'x':
			{
				// \xHH - exactly two hex digits
				if ((_position + 1) >= _source.Length || !IsHexDigit(CurrentChar()) || !IsHexDigit(Peek(1)))
				{
					throw new AssemblyException(
						startLine,
						startColumn,
						"\\x escape requires exactly two hex digits (e.g. \\x41)"
					);
				}
				ulong hi = HexDigitValue(CurrentChar());
				ulong lo = HexDigitValue(Peek(1));
				Advance(2);
				return (hi << 4) | lo;
			}

			default:
				throw new AssemblyException(startLine, startColumn, $"Unknown escape sequence '\\{code}'");
		}
	}

	private static bool IsHexDigit(char c)
	{
		return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
	}

	private static ulong HexDigitValue(char c)
	{
		return c switch
		{
			>= '0' and <= '9' => (ulong)(c - '0'),
			>= 'a' and <= 'f' => (ulong)((c - 'a') + 10),
			_ => (ulong)((c - 'A') + 10),
		};
	}

	private Token ReadString(int startLine, int startColumn)
	{
		Advance(); // skip opening quote
		var sb = new StringBuilder();

		while (true)
		{
			if (_position >= _source.Length || CurrentChar() is '\n' or '\r')
			{
				throw new AssemblyException(startLine, startColumn, "Unterminated string literal");
			}

			if (CurrentChar() == '"')
			{
				Advance(); // skip closing quote
				break;
			}

			if (CurrentChar() == '\\')
			{
				Advance(); // skip backslash
				sb.Append((char)ReadEscapeSequence(startLine, startColumn));
			}
			else
			{
				sb.Append(CurrentChar());
				Advance();
			}
		}

		return new Token(TokenKind.StringLiteral, sb.ToString(), 0, startLine, startColumn);
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
			_position++;
		}
		if (_position < _source.Length && CurrentChar() == '\n')
		{
			_position++;
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
			'%' => TokenKind.Percent,
			'$' => TokenKind.Dollar,
			'&' => TokenKind.Ampersand,
			'^' => TokenKind.Caret,
			'|' => TokenKind.Pipe,
			'~' => TokenKind.Tilde,
			'[' => TokenKind.LeftBracket,
			']' => TokenKind.RightBracket,
			_ => TokenKind.EndOfFile,
		};
		return kind != TokenKind.EndOfFile;
	}
}
