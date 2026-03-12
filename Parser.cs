namespace im8000asm;

public abstract record ParsedOperand;

public record StringLiteralOperand(string Value) : ParsedOperand;

public record AltNarrowRegisterOperand(NarrowRegister Register) : ParsedOperand;

public record AltWideRegisterOperand(WideRegister Register) : ParsedOperand;

public record SpecialRegisterOperand(SpecialRegister Register) : ParsedOperand;

public record ConditionOperand(BranchCondition Condition) : ParsedOperand;

public record IndirectOperand(WideRegister Register) : ParsedOperand;

public record IndexedOperand(WideRegister Register, ExpressionNode Displacement) : ParsedOperand;

public record DirectMemoryOperand(ExpressionNode Address) : ParsedOperand;

public record ImmediateOrRegisterOperand(ExpressionNode Expression) : ParsedOperand;

public abstract record ParsedStatement(int Line, int Column);

public record LabelStatement(string Name, int Line, int Column) : ParsedStatement(Line, Column);

public record InstructionStatement(Mnemonic Mnemonic, OperandSize? Size, ParsedOperand[] Operands, int Line, int Column)
	: ParsedStatement(Line, Column);

public record DirectiveStatement(
	Directive Directive,
	ParsedOperand[] Operands,
	int Line,
	int Column,
	string LabelName = ""
) : ParsedStatement(Line, Column)
{
	public bool HasLabel => LabelName.Length > 0;
}

public class Parser
{
	private readonly List<Token> _tokens;
	private string _pendingLabel = string.Empty;
	private int _position;

	public Parser(List<Token> tokens)
	{
		_tokens = tokens;
	}

	public List<ParsedStatement> Parse()
	{
		List<ParsedStatement> statements = [];

		while (!AtEnd())
		{
			SkipNewLines();
			if (AtEnd())
			{
				break;
			}
			statements.AddRange(ParseNextStatements());
		}

		return statements;
	}

	private IEnumerable<ParsedStatement> ParseNextStatements()
	{
		Token token = CurrentToken();
		if (token.Kind == TokenKind.EndOfFile)
		{
			yield break;
		}

		// Label
		if (token.Kind == TokenKind.Identifier && PeekToken().Kind == TokenKind.Colon)
		{
			_pendingLabel = token.Text.ToUpperInvariant();
			Advance(2); // consume label and colon
			yield return new LabelStatement(_pendingLabel, token.Line, token.Column);

			if (AtEndOfLine())
			{
				yield break;
			}
			token = CurrentToken();
		}

		if (token.Kind != TokenKind.Identifier)
		{
			Advance();
			yield break;
		}

		(string name, OperandSize? size) = SplitMnemonicText(token.Text);

		// Directive
		if (Keywords.TryParseDirective(name, out Directive directive))
		{
			Advance();
			string labelForDirective = _pendingLabel;
			_pendingLabel = "";
			yield return ParseDirectiveStatement(directive, token.Line, token.Column, labelForDirective);
			yield break;
		}

		// Mnemonic
		if (!Keywords.TryParseMnemonic(name, out Mnemonic mnemonic))
		{
			throw new AssemblyException(token.Line, token.Column, $"Unknown mnemonic or directive '{token.Text}'");
		}

		_pendingLabel = "";
		Advance();
		ParsedOperand[] operands = ParseOperandList(mnemonic);
		yield return new InstructionStatement(mnemonic, size, operands, token.Line, token.Column);
	}

	private DirectiveStatement ParseDirectiveStatement(Directive directive, int line, int column, string labelName)
	{
		ParsedOperand[] operands = ParseOperandList(directive: directive);
		return new DirectiveStatement(directive, operands, line, column, labelName);
	}

	private ParsedOperand[] ParseOperandList(Mnemonic? mnemonic = null, Directive? directive = null)
	{
		if (AtEndOfLine())
		{
			return [];
		}

		List<ParsedOperand> list =
		[
			ParseOperand(mnemonic, directive),
		];

		while (!AtEndOfLine() && CurrentToken().Kind == TokenKind.Comma)
		{
			Advance();
			list.Add(ParseOperand(mnemonic, directive));
		}

		return list.ToArray();
	}

	private ParsedOperand ParseOperand(Mnemonic? mnemonic = null, Directive? directive = null)
	{
		Token token = CurrentToken();

		// String literal for directives
		if (directive.HasValue &&
			Keywords.StringAcceptingDirectives.Contains(directive.Value) &&
			token.Kind == TokenKind.StringLiteral)
		{
			Advance();
			return new StringLiteralOperand(token.Text);
		}

		// Alternate registers (e.g., A', HL')
		if (token.Kind == TokenKind.Identifier && token.Text.EndsWith('\''))
		{
			Advance();
			string name = token.Text[..^1].ToUpperInvariant();
			if (Registers.TryParseNarrow(name, out NarrowRegister narrow))
			{
				return new AltNarrowRegisterOperand(narrow);
			}
			if (Registers.TryParseWide(name, out WideRegister wide))
			{
				return new AltWideRegisterOperand(wide);
			}
			throw new AssemblyException(token.Line, token.Column, $"'{token.Text}' is not a valid alternate register");
		}

		// Branch condition operand
		if (mnemonic.HasValue &&
			Keywords.BranchMnemonics.Contains(mnemonic.Value) &&
			token.Kind == TokenKind.Identifier &&
			Enum.TryParse(token.Text, true, out BranchCondition condition))
		{
			Advance();
			return new ConditionOperand(condition);
		}

		// Special register operand
		if (token.Kind == TokenKind.Identifier && Enum.TryParse(token.Text, true, out SpecialRegister special))
		{
			Advance();
			return new SpecialRegisterOperand(special);
		}

		// Memory operand
		if (token.Kind == TokenKind.LeftBracket)
		{
			Advance();
			return ParseMemoryOperand();
		}

		// Fallback: expression operand
		ExpressionNode expr = ExpressionParser.Parse(_tokens, ref _position);
		return new ImmediateOrRegisterOperand(expr);
	}

	private ParsedOperand ParseMemoryOperand()
	{
		Token token = CurrentToken();

		(string name, _) = SplitMnemonicText(token.Text);
		if (!Registers.TryParseWide(name, out WideRegister register))
		{
			ExpressionNode address = ExpressionParser.Parse(_tokens, ref _position);
			Expect(TokenKind.RightBracket);
			return new DirectMemoryOperand(address);
		}

		Advance();
		bool hasOffset = CurrentToken().Kind is TokenKind.Plus or TokenKind.Minus;
		if (hasOffset)
		{
			bool negate = CurrentToken().Kind == TokenKind.Minus;
			Advance();
			ExpressionNode displacement = ExpressionParser.Parse(_tokens, ref _position);
			Expect(TokenKind.RightBracket);
			if (negate)
			{
				displacement = new UnaryExpressionNode('-', displacement, displacement.Line, displacement.Column);
			}
			return new IndexedOperand(register, displacement);
		}

		if (Registers.RequiresDisplacement(register))
		{
			throw new AssemblyException(
				token.Line,
				token.Column,
				"Index register in '[' requires a displacement, e.g. [IX+0]"
			);
		}

		Expect(TokenKind.RightBracket);
		return new IndirectOperand(register);
	}

	public static (string Name, OperandSize? Size) SplitMnemonicText(string text)
	{
		if (text.StartsWith('.'))
		{
			text = text[1..];
		}
		int dotIndex = text.LastIndexOf('.');
		if (dotIndex < 0)
		{
			return (text.ToUpperInvariant(), null);
		}

		string suffix = text[(dotIndex + 1)..].ToUpperInvariant();
		OperandSize? size = suffix switch
		{
			"B" => OperandSize.Byte,
			"W" => OperandSize.Word,
			"D" => OperandSize.Dword,
			_ => null,
		};

		return (text[..dotIndex].ToUpperInvariant(), size);
	}

	private Token CurrentToken()
	{
		return _tokens[_position];
	}

	private Token PeekToken()
	{
		return (_position + 1) < _tokens.Count ? _tokens[_position + 1] : _tokens[^1];
	}

	private void Advance(int count = 1)
	{
		_position += count;
	}

	private bool AtEnd()
	{
		return CurrentToken().Kind == TokenKind.EndOfFile;
	}

	private bool AtEndOfLine()
	{
		return CurrentToken().Kind is TokenKind.NewLine or TokenKind.EndOfFile;
	}

	private void SkipNewLines()
	{
		while (!AtEnd() && CurrentToken().Kind == TokenKind.NewLine)
		{
			Advance();
		}
	}

	private void Expect(TokenKind kind)
	{
		Token token = CurrentToken();
		if (token.Kind != kind)
		{
			throw new AssemblyException(
				token.Line,
				token.Column,
				$"Expected {kind}, got {token.Kind} ('{token.Text}')"
			);
		}
		Advance();
	}
}
