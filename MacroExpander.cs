namespace im8000asm;

public static class MacroExpander
{
	public static List<Token> Expand(List<Token> tokens)
	{
		Dictionary<string, MacroDefinition> macros = new(StringComparer.OrdinalIgnoreCase);
		int invocationCounter = 0;

		return ExpandTokens(tokens, macros, ref invocationCounter, true);
	}

	private static List<Token> ExpandTokens(
		List<Token> tokens,
		Dictionary<string, MacroDefinition> macros,
		ref int invocationCounter,
		bool allowDefinitions
	)
	{
		List<Token> output = [];
		int pos = 0;

		while (pos < tokens.Count)
		{
			Token token = tokens[pos];

			if (token.Kind == TokenKind.EndOfFile)
			{
				output.Add(token);
				break;
			}

			// Detect "<name> MACRO" or "<name> \n MACRO"
			if (token.Kind == TokenKind.Identifier && allowDefinitions)
			{
				int lookahead = pos + 1;
				if (lookahead < tokens.Count &&
					tokens[lookahead].Kind == TokenKind.Identifier &&
					tokens[lookahead].Text.Equals("MACRO", StringComparison.OrdinalIgnoreCase))
				{
					string macroName = token.Text;

					if (macros.ContainsKey(macroName))
					{
						throw new AssemblyException(
							token.Line,
							token.Column,
							$"Macro '{macroName}' is already defined"
						);
					}

					// Consume name + MACRO keyword
					pos += 2;

					// Collect parameter names
					List<string> parameters = ConsumeIdentifierList(tokens, ref pos);

					// Advance past the newline that ends the header
					SkipNewLines(tokens, ref pos);

					// Capture body
					(List<Token> bodyTokens, List<string> locals) = CaptureMacroBody(
						tokens,
						ref pos,
						token.Line,
						token.Column
					);

					macros[macroName] = new MacroDefinition(macroName, parameters, locals, bodyTokens);

					continue;
				}
			}

			// Detect macro invocation: NAME  arg1, arg2, ...
			if (token.Kind == TokenKind.Identifier && macros.TryGetValue(token.Text, out MacroDefinition? macro))
			{
				pos++; // consume the macro name token

				// Collect arguments
				List<List<Token>> args = ConsumeArgumentList(tokens, ref pos);

				if (args.Count != macro.Parameters.Count)
				{
					throw new AssemblyException(
						token.Line,
						token.Column,
						$"Macro '{macro.Name}' expects {macro.Parameters.Count} argument(s), got {args.Count}"
					);
				}

				invocationCounter++;
				string suffix = $"??{invocationCounter:D4}";

				// Build argument map: parameter name -> token list
				Dictionary<string, List<Token>> argMap = new(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < macro.Parameters.Count; i++)
				{
					argMap[macro.Parameters[i]] = args[i];
				}

				// Expand the body with substitutions
				List<Token> expanded = ExpandBody(macro, argMap, suffix);

				// Recursively expand (macros can call other macros, but not themselves)
				List<Token> recursed = ExpandTokens(expanded, macros, ref invocationCounter, false);

				// Append without the trailing EOF the recursive call may have added.
				foreach (Token t in recursed)
				{
					if (t.Kind != TokenKind.EndOfFile)
					{
						output.Add(t);
					}
				}

				// Ensure expansion ends with a newline so the next statement parses cleanly.
				if (output.Count > 0 && output[^1].Kind != TokenKind.NewLine)
				{
					output.Add(new Token(TokenKind.NewLine, "\\n", 0, token.Line, token.Column));
				}

				continue;
			}

			output.Add(token);
			pos++;
		}

		return output;
	}

	/// <summary>
	///     Reads tokens from pos until ENDM, collecting LOCAL declarations and returning body tokens.
	/// </summary>
	private static (List<Token> Body, List<string> Locals) CaptureMacroBody(
		List<Token> tokens,
		ref int pos,
		int macroLine,
		int macroColumn
	)
	{
		List<Token> body = [];
		List<string> locals = [];

		while (pos < tokens.Count)
		{
			Token token = tokens[pos];

			if (token.Kind == TokenKind.EndOfFile)
			{
				throw new AssemblyException(macroLine, macroColumn, "Unterminated macro definition");
			}

			if (token.Kind == TokenKind.Identifier)
			{
				if (string.Equals(token.Text, "ENDM", StringComparison.OrdinalIgnoreCase))
				{
					pos++;
					return (body, locals);
				}

				if (string.Equals(token.Text, "MACRO", StringComparison.OrdinalIgnoreCase))
				{
					// A bare MACRO keyword in a body would mean a nested definition.
					throw new AssemblyException(token.Line, token.Column, "Nested MACRO definitions are not allowed");
				}

				// LOCAL label1, label2, ...
				if (string.Equals(token.Text, "LOCAL", StringComparison.OrdinalIgnoreCase))
				{
					pos++; // consume LOCAL
					List<string> names = ConsumeIdentifierList(tokens, ref pos);
					locals.AddRange(names);
					SkipNewLines(tokens, ref pos);
					continue;
				}
			}

			body.Add(token);
			pos++;
		}

		throw new AssemblyException(macroLine, macroColumn, "Unterminated macro definition");
	}

	private static List<Token> ExpandBody(
		MacroDefinition macro,
		Dictionary<string, List<Token>> argMap,
		string localSuffix
	)
	{
		List<Token> result = [];

		foreach (Token token in macro.Body)
		{
			if (token.Kind != TokenKind.Identifier)
			{
				result.Add(token);
				continue;
			}

			string name = token.Text;

			// Argument substitution takes priority over local renaming.
			if (argMap.TryGetValue(name, out List<Token>? argTokens))
			{
				// Set all argument tokens with the call-site line so error messages point somewhere.
				foreach (Token argToken in argTokens)
				{
					result.Add(
						argToken with
						{
							Line = token.Line,
							Column = token.Column,
						}
					);
				}
				continue;
			}

			// Local label renaming: both label definitions and references within the body.
			if (macro.Locals.Contains(name, StringComparer.OrdinalIgnoreCase))
			{
				result.Add(
					token with
					{
						Text = name + localSuffix,
					}
				);
				continue;
			}

			result.Add(token);
		}

		return result;
	}

	/// <summary>
	///     Reads a comma-separated list of plain identifiers from the current position
	///     up to the next newline or EOF. Used for MACRO parameter lists and LOCAL lists.
	/// </summary>
	private static List<string> ConsumeIdentifierList(List<Token> tokens, ref int pos)
	{
		List<string> names = [];

		while (pos < tokens.Count)
		{
			Token token = tokens[pos];

			if (token.Kind is TokenKind.NewLine or TokenKind.EndOfFile)
			{
				break;
			}

			if (token.Kind == TokenKind.Comma)
			{
				pos++;
				continue;
			}

			if (token.Kind == TokenKind.Identifier)
			{
				names.Add(token.Text);
				pos++;
				continue;
			}

			// Unexpected token, stop (will be an error downstream if it matters).
			break;
		}

		return names;
	}

	/// <summary>
	///     Reads a comma-separated list of argument token sequences.
	///     Each argument is all tokens up to the next top-level comma or newline/EOF.
	///     Brackets and parentheses are tracked so  [IX+1]  counts as one argument.
	/// </summary>
	private static List<List<Token>> ConsumeArgumentList(List<Token> tokens, ref int pos)
	{
		List<List<Token>> args = [];
		List<Token> current = [];
		int depth = 0;

		while (pos < tokens.Count)
		{
			Token token = tokens[pos];

			if (token.Kind is TokenKind.NewLine or TokenKind.EndOfFile)
			{
				break;
			}

			if (token.Kind is TokenKind.LeftParen or TokenKind.LeftBracket)
			{
				depth++;
				current.Add(token);
				pos++;
				continue;
			}

			if (token.Kind is TokenKind.RightParen or TokenKind.RightBracket)
			{
				depth--;
				current.Add(token);
				pos++;
				continue;
			}

			if (token.Kind == TokenKind.Comma && depth == 0)
			{
				args.Add(TrimTokens(current));
				current = [];
				pos++;
				continue;
			}

			current.Add(token);
			pos++;
		}

		// Commit the last argument (even if empty, so zero-arg calls work).
		List<Token> last = TrimTokens(current);
		if (last.Count > 0 || args.Count > 0)
		{
			args.Add(last);
		}

		return args;
	}

	private static List<Token> TrimTokens(List<Token> tokens)
	{
		return tokens; // lexer already strips whitespace between tokens, but in case we change that
	}

	private static void SkipNewLines(List<Token> tokens, ref int pos)
	{
		while (pos < tokens.Count && tokens[pos].Kind == TokenKind.NewLine)
		{
			pos++;
		}
	}

	private sealed class MacroDefinition(string name, List<string> parameters, List<string> locals, List<Token> body)
	{
		public string Name { get; } = name;
		public List<string> Parameters { get; } = parameters;
		public List<string> Locals { get; } = locals;
		public List<Token> Body { get; } = body;
	}
}
