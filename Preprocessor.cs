using System.Text.RegularExpressions;

namespace im8000asm;

public record SourceLine(string Text, string File, int FileLineNumber);

public static class Preprocessor
{
	private static readonly Regex IncludePattern = new(
		"""^\s*\.?INCLUDE\s+"([^"]+)"\s*(?:;.*)?$""",
		RegexOptions.IgnoreCase
	);

	private static readonly Regex IncbinPattern = new("""^(\s*\.?INCBIN\s+")([^"]+)(")""", RegexOptions.IgnoreCase);

	private static readonly Regex MacroDefPattern = new(
		"""^\s*\.?MACRO\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(([^)]*)\))?\s*(?:;.*)?$""",
		RegexOptions.IgnoreCase
	);

	private static readonly Regex EndMacroPattern = new("""^\s*\.?ENDMACRO\s*(?:;.*)?$""", RegexOptions.IgnoreCase);

	private static readonly Regex MacroCallPattern = new(
		"""^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*(?:;.*)?$""",
		RegexOptions.IgnoreCase
	);

	private static readonly Regex StructDefPattern = new(
		"""^\s*\.?STRUCT\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:;.*)?$""",
		RegexOptions.IgnoreCase
	);

	private static readonly Regex EndStructPattern = new("""^\s*\.?ENDSTRUCT\s*(?:;.*)?$""", RegexOptions.IgnoreCase);

	private static readonly Regex StructMemberPattern = new(
		"""^\s*\.([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(\d+)\s*(?:;.*)?$""",
		RegexOptions.IgnoreCase
	);

	public static SourceLine[] Process(string rootPath)
	{
		string fullPath = Path.GetFullPath(rootPath);
		HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> stack = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, MacroDefinition> macros = new(StringComparer.OrdinalIgnoreCase);
		List<SourceLine> result = [];
		int expansionCounter = 0;

		ProcessFile(fullPath, included, stack, macros, result, ref expansionCounter, null, 0);

		return result.ToArray();
	}

	private static void ProcessFile(
		string fullPath,
		HashSet<string> included,
		HashSet<string> stack,
		Dictionary<string, MacroDefinition> macros,
		List<SourceLine> result,
		ref int expansionCounter,
		string? callerFile,
		int callerLine
	)
	{
		if (!File.Exists(fullPath))
		{
			string location = callerFile is not null ? $"{callerFile}:{callerLine}" : fullPath;
			throw new AssemblyException(
				callerLine,
				0,
				$"Cannot find included file: '{fullPath}' (included from {location})"
			);
		}

		if (!stack.Add(fullPath))
		{
			throw new AssemblyException(
				callerLine,
				0,
				$"Circular INCLUDE detected: '{fullPath}' is already being assembled"
			);
		}

		if (!included.Add(fullPath))
		{

			stack.Remove(fullPath);
			return;
		}

		string[] lines = File.ReadAllLines(fullPath);
		string directory = Path.GetDirectoryName(fullPath)!;

		ProcessLines(lines, fullPath, directory, included, stack, macros, result, ref expansionCounter);

		stack.Remove(fullPath);
	}

	private static void ProcessLines(
		string[] lines,
		string sourceFile,
		string directory,
		HashSet<string> included,
		HashSet<string> stack,
		Dictionary<string, MacroDefinition> macros,
		List<SourceLine> result,
		ref int expansionCounter
	)
	{
		int i = 0;

		while (i < lines.Length)
		{
			string line = lines[i];
			int lineNumber = i + 1;
			i++;

			Match includeMatch = IncludePattern.Match(line);
			if (includeMatch.Success)
			{
				string includedPath = includeMatch.Groups[1].Value;
				string resolvedPath = Path.GetFullPath(Path.Combine(directory, includedPath));
				ProcessFile(
					resolvedPath,
					included,
					stack,
					macros,
					result,
					ref expansionCounter,
					sourceFile,
					lineNumber
				);
				continue;
			}

			Match macroDefMatch = MacroDefPattern.Match(line);
			if (macroDefMatch.Success)
			{
				string macroName = macroDefMatch.Groups[1].Value;

				if (macros.ContainsKey(macroName))
				{
					throw new AssemblyException(lineNumber, 0, $"Macro '{macroName}' is already defined");
				}

				string[] parameters = SplitCommaList(macroDefMatch.Groups[2].Value);
				string[] bodyLines = CaptureMacroBody(lines, ref i, lineNumber);
				macros[macroName] = new MacroDefinition(macroName, parameters, bodyLines, sourceFile, lineNumber);
				continue;
			}

			Match structDefMatch = StructDefPattern.Match(line);
			if (structDefMatch.Success)
			{
				string structName = structDefMatch.Groups[1].Value;
				EmitStructEqus(lines, ref i, structName, sourceFile, lineNumber, result);
				continue;
			}

			Match incbinMatch = IncbinPattern.Match(line);
			if (incbinMatch.Success)
			{
				string relativePath = incbinMatch.Groups[2].Value;
				string absolutePath = Path.GetFullPath(Path.Combine(directory, relativePath));
				line = incbinMatch.Groups[1].Value + absolutePath + incbinMatch.Groups[3].Value;
				result.Add(new SourceLine(line, sourceFile, lineNumber));
				continue;
			}

			Match callMatch = MacroCallPattern.Match(line);
			if (callMatch.Success && macros.TryGetValue(callMatch.Groups[1].Value, out MacroDefinition macro))
			{
				expansionCounter++;
				string[] args = SplitCommaList(callMatch.Groups[2].Value);
				ExpandMacro(macro, args, expansionCounter, lineNumber, result);
				continue;
			}

			result.Add(new SourceLine(line, sourceFile, lineNumber));
		}
	}

	private static string[] CaptureMacroBody(string[] lines, ref int i, int macroStartLine)
	{
		List<string> body = [];

		while (i < lines.Length)
		{
			string bodyLine = lines[i];
			i++;

			if (EndMacroPattern.IsMatch(bodyLine))
			{
				return body.ToArray();
			}

			if (MacroDefPattern.IsMatch(bodyLine))
			{
				throw new AssemblyException(macroStartLine, 0, "Nested MACRO definitions are not allowed");
			}

			body.Add(bodyLine);
		}

		throw new AssemblyException(macroStartLine, 0, "Unterminated MACRO definition (missing ENDMACRO)");
	}

	private static void ExpandMacro(
		MacroDefinition macro,
		string[] args,
		int invocationId,
		int callLine,
		List<SourceLine> result
	)
	{
		if (args.Length != macro.Parameters.Length)
		{
			throw new AssemblyException(
				callLine,
				0,
				$"Macro '{macro.Name}' expects {macro.Parameters.Length} argument(s), got {args.Length}"
			);
		}

		foreach (string rawLine in macro.BodyLines)
		{
			string expanded = rawLine;

			for (int i = 0; i < macro.Parameters.Length; i++)
			{
				expanded = expanded.Replace($"%{macro.Parameters[i]}", args[i], StringComparison.OrdinalIgnoreCase);
			}

			for (int i = 0; i < args.Length; i++)
			{
				expanded = expanded.Replace($"%{i + 1}", args[i]);
			}

			expanded = expanded.Replace("@", $"{invocationId}");

			result.Add(new SourceLine(expanded, macro.SourceFile, macro.SourceLine));
		}
	}

	private static void EmitStructEqus(
		string[] lines,
		ref int i,
		string structName,
		string sourceFile,
		int structStartLine,
		List<SourceLine> result
	)
	{

		result.Add(new SourceLine($"{structName}: EQU 0", sourceFile, structStartLine));

		long offset = 0;

		while (i < lines.Length)
		{
			string memberLine = lines[i];
			int memberLineNumber = i + 1;
			i++;

			if (EndStructPattern.IsMatch(memberLine))
			{
				result.Add(new SourceLine($"{structName}._size: EQU {offset}", sourceFile, structStartLine));
				return;
			}

			if (string.IsNullOrWhiteSpace(memberLine) || memberLine.TrimStart().StartsWith(';'))
			{
				continue;
			}

			Match memberMatch = StructMemberPattern.Match(memberLine);
			if (!memberMatch.Success)
			{
				throw new AssemblyException(
					memberLineNumber,
					0,
					$"Invalid struct member in '{structName}': expected '.name: <size>', got '{memberLine.Trim()}'"
				);
			}

			string memberName = memberMatch.Groups[1].Value;
			long memberSize = long.Parse(memberMatch.Groups[2].Value);

			result.Add(new SourceLine($"{structName}.{memberName}: EQU {offset}", sourceFile, structStartLine));

			offset += memberSize;
		}

		throw new AssemblyException(structStartLine, 0, "Unterminated STRUCT definition (missing ENDSTRUCT)");
	}

	private static string[] SplitCommaList(string raw)
	{
		return string.IsNullOrWhiteSpace(raw)
			? []
			: raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
	}

	private struct MacroDefinition(
		string name,
		string[] parameters,
		string[] bodyLines,
		string sourceFile,
		int sourceLine
	)
	{
		public readonly string Name = name;
		public readonly string[] Parameters = parameters;
		public readonly string[] BodyLines = bodyLines;
		public readonly string SourceFile = sourceFile;
		public readonly int SourceLine = sourceLine;
	}
}
