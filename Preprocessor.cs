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

	public static SourceLine[] Process(string rootPath)
	{
		string fullPath = Path.GetFullPath(rootPath);
		HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> stack = new(StringComparer.OrdinalIgnoreCase);
		List<SourceLine> result = [];

		ProcessFile(fullPath, included, stack, result, null, 0);

		return result.ToArray();
	}

	private static void ProcessFile(
		string fullPath,
		HashSet<string> included,
		HashSet<string> stack,
		List<SourceLine> result,
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

		ProcessLines(lines, fullPath, directory, included, stack, result);

		stack.Remove(fullPath);
	}

	private static void ProcessLines(
		string[] lines,
		string sourceFile,
		string directory,
		HashSet<string> included,
		HashSet<string> stack,
		List<SourceLine> result
	)
	{
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			int lineNumber = i + 1;

			Match includeMatch = IncludePattern.Match(line);
			if (includeMatch.Success)
			{
				string includedPath = includeMatch.Groups[1].Value;
				string resolvedPath = Path.GetFullPath(Path.Combine(directory, includedPath));
				ProcessFile(resolvedPath, included, stack, result, sourceFile, lineNumber);
				continue;
			}

			Match incbinMatch = IncbinPattern.Match(line);
			if (incbinMatch.Success)
			{
				string relativePath = incbinMatch.Groups[2].Value;
				string absolutePath = Path.GetFullPath(Path.Combine(directory, relativePath));
				line = incbinMatch.Groups[1].Value + absolutePath + incbinMatch.Groups[3].Value;
			}

			result.Add(new SourceLine(line, sourceFile, lineNumber));
		}
	}
}
