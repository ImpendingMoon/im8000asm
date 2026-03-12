namespace im8000asm;

public abstract record ExpressionNode(int Line, int Column);

public record NumberLiteralNode(long Value, int Line, int Column) : ExpressionNode(Line, Column);

public record CurrentAddressNode(int Line, int Column) : ExpressionNode(Line, Column);

public record SymbolReferenceNode(string Name, int Line, int Column) : ExpressionNode(Line, Column);

public record UnaryExpressionNode(char Operator, ExpressionNode Operand, int Line, int Column)
	: ExpressionNode(Line, Column);

public record BinaryExpressionNode(
	string Operator,
	ExpressionNode Left,
	ExpressionNode Right,
	int Line,
	int Column
) : ExpressionNode(Line, Column);

public static class ExpressionParser
{
	public static ExpressionNode Parse(List<Token> tokens, ref int position)
	{
		return ParseBitwise(tokens, ref position);
	}

	private static ExpressionNode ParseBitwise(List<Token> tokens, ref int pos)
	{
		ExpressionNode left = ParseAddSub(tokens, ref pos);
		while (PeekKind(tokens, pos) is TokenKind.Ampersand or TokenKind.Caret or TokenKind.Pipe)
		{
			Token op = tokens[pos++];
			ExpressionNode right = ParseAddSub(tokens, ref pos);
			left = new BinaryExpressionNode(op.Text, left, right, op.Line, op.Column);
		}
		return left;
	}

	private static ExpressionNode ParseAddSub(List<Token> tokens, ref int pos)
	{
		ExpressionNode left = ParseMulDiv(tokens, ref pos);
		while (PeekKind(tokens, pos) is TokenKind.Plus or TokenKind.Minus)
		{
			Token op = tokens[pos++];
			ExpressionNode right = ParseMulDiv(tokens, ref pos);
			left = new BinaryExpressionNode(op.Text, left, right, op.Line, op.Column);
		}
		return left;
	}

	private static ExpressionNode ParseMulDiv(List<Token> tokens, ref int pos)
	{
		ExpressionNode left = ParseShift(tokens, ref pos);
		while (PeekKind(tokens, pos) is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
		{
			Token op = tokens[pos++];
			ExpressionNode right = ParseShift(tokens, ref pos);
			left = new BinaryExpressionNode(op.Text, left, right, op.Line, op.Column);
		}
		return left;
	}

	private static ExpressionNode ParseShift(List<Token> tokens, ref int pos)
	{
		ExpressionNode left = ParseUnary(tokens, ref pos);
		while (PeekKind(tokens, pos) is TokenKind.ShiftLeft or TokenKind.ShiftRight)
		{
			Token op = tokens[pos++];
			ExpressionNode right = ParseUnary(tokens, ref pos);
			left = new BinaryExpressionNode(op.Text, left, right, op.Line, op.Column);
		}
		return left;
	}

	private static ExpressionNode ParseUnary(List<Token> tokens, ref int pos)
	{
		Token token = Peek(tokens, pos);

		if (token.Kind is TokenKind.Minus or TokenKind.Tilde)
		{
			pos++;
			ExpressionNode operand = ParseUnary(tokens, ref pos);
			return new UnaryExpressionNode(token.Text[0], operand, token.Line, token.Column);
		}

		if (token.Kind == TokenKind.Plus)
		{
			pos++;
			return ParseUnary(tokens, ref pos);
		}

		return ParsePrimary(tokens, ref pos);
	}

	private static ExpressionNode ParsePrimary(List<Token> tokens, ref int pos)
	{
		Token token = Peek(tokens, pos);
		pos++;
		return token.Kind switch
		{
			TokenKind.Number => new NumberLiteralNode((long)token.NumericValue, token.Line, token.Column),
			TokenKind.Dollar => new CurrentAddressNode(token.Line, token.Column),
			TokenKind.Identifier => new SymbolReferenceNode(token.Text.ToUpperInvariant(), token.Line, token.Column),
			TokenKind.LeftParen => ParseParenExpression(tokens, ref pos, token),
			_ => throw new AssemblyException(
				token.Line,
				token.Column,
				$"Unexpected token '{token.Text}' in expression"
			),
		};
	}

	private static ExpressionNode ParseParenExpression(List<Token> tokens, ref int pos, Token open)
	{
		ExpressionNode inner = ParseAddSub(tokens, ref pos);
		if (Peek(tokens, pos).Kind != TokenKind.RightParen)
		{
			throw new AssemblyException(open.Line, open.Column, "Expected ')' to close sub-expression");
		}
		pos++;
		return inner;
	}

	private static Token Peek(List<Token> tokens, int pos)
	{
		return pos < tokens.Count ? tokens[pos] : tokens[^1];
	}

	private static TokenKind PeekKind(List<Token> tokens, int pos)
	{
		return Peek(tokens, pos).Kind;
	}
}

public static class ExpressionEvaluator
{
	public static long Evaluate(
		ExpressionNode expr,
		IReadOnlyDictionary<string, uint> symbols,
		uint currentAddress,
		List<Diagnostic> diagnostics
	)
	{
		long Eval(ExpressionNode node)
		{
			return node switch
			{
				NumberLiteralNode n => n.Value,
				CurrentAddressNode => currentAddress,
				SymbolReferenceNode s => ResolveSymbol(s),
				UnaryExpressionNode u => u.Operator switch
				{
					'-' => -Eval(u.Operand),
					'~' => ~Eval(u.Operand),
					_ => Eval(u.Operand),
				},
				BinaryExpressionNode b => EvalBinary(b),
				_ => AddError(node.Line, node.Column, $"Unknown expression node '{node.GetType().Name}'", diagnostics),
			};
		}

		long EvalBinary(BinaryExpressionNode b)
		{
			long left = Eval(b.Left);
			long right = Eval(b.Right);
			return b.Operator switch
			{
				"+" => left + right,
				"-" => left - right,
				"*" => left * right,
				"/" => right != 0 ? left / right : AddError(b.Line, b.Column, "Division by zero", diagnostics),
				"%" => right != 0 ? left % right : AddError(b.Line, b.Column, "Modulo by zero", diagnostics),
				"<<" => left << (int)(right & 63),
				">>" => left >> (int)(right & 63),
				"&" => left & right,
				"^" => left ^ right,
				"|" => left | right,
				_ => AddError(b.Line, b.Column, $"Unknown operator '{b.Operator}'", diagnostics),
			};
		}

		long ResolveSymbol(SymbolReferenceNode symbol)
		{
			return symbols.TryGetValue(symbol.Name, out uint value)
				? value
				: AddError(symbol.Line, symbol.Column, $"Undefined symbol '{symbol.Name}'", diagnostics);
		}

		return Eval(expr);
	}

	private static long AddError(int line, int column, string message, List<Diagnostic> diagnostics)
	{
		diagnostics.Add(new Diagnostic(line, column, DiagnosticSeverity.Error, message));
		return 0;
	}
}
