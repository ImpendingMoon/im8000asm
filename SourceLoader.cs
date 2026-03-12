using System.Text.RegularExpressions;

namespace im8000asm;

public record SourceLine(string Text, string File, int FileLineNumber);

public static class SourceLoader
{
	private static readonly Regex IncludePattern = new(
		"""
		^\s*\.?INCLUDE\s+"([^"]+)"
		""",
		RegexOptions.IgnoreCase
	);

	private static readonly Regex IncbinPattern = new("""^(\s*\.?INCBIN\s+")([^"]+)(")""", RegexOptions.IgnoreCase);

	public static SourceLine[] Load(string rootPath)
	{
		string fullPath = Path.GetFullPath(rootPath);
		HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
		List<SourceLine> result = [];

		LoadFile(fullPath, visited, result, null, 0);

		return result.ToArray();
	}

	private static void LoadFile(
		string fullPath,
		HashSet<string> visited,
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

		if (!visited.Add(fullPath))
		{
			throw new AssemblyException(
				callerLine,
				0,
				$"Circular INCLUDE detected: '{fullPath}' is already being assembled"
			);
		}

		string[] lines = File.ReadAllLines(fullPath);
		string directory = Path.GetDirectoryName(fullPath)!;

		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			string line = lines[lineIndex];
			int fileLineNumber = lineIndex + 1;

			Match includeMatch = IncludePattern.Match(line);
			if (includeMatch.Success)
			{
				string includedPath = includeMatch.Groups[1].Value;
				string resolvedPath = Path.GetFullPath(Path.Combine(directory, includedPath));
				LoadFile(resolvedPath, visited, result, fullPath, fileLineNumber);
				continue;
			}

			// Rewrite INCBIN paths to absolute so the assembler can find the file regardless of
			// the working directory at assembly time.
			Match incbinMatch = IncbinPattern.Match(line);
			if (incbinMatch.Success)
			{
				string relativePath = incbinMatch.Groups[2].Value;
				string absolutePath = Path.GetFullPath(Path.Combine(directory, relativePath));
				line = incbinMatch.Groups[1].Value + absolutePath + incbinMatch.Groups[3].Value;
			}

			result.Add(new SourceLine(line, fullPath, fileLineNumber));
		}

		visited.Remove(fullPath);
	}
}
