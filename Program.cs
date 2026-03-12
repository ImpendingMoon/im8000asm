using System.Diagnostics;
using System.Text;

namespace im8000asm;

internal static class Program
{
	private static int Main(string[] args)
	{
		Stopwatch stopwatch = new();
		stopwatch.Start();

		if (args.Length < 1)
		{
			Console.Error.WriteLine("Usage: assembler <input.asm> [-o <output.bin>] [-E]");
			return 1;
		}

		string inputPath = args[0];
		string outputPath = "a.bin";
		bool preprocessOnly = false;

		for (int index = 1; index < args.Length; index++)
		{
			if (args[index] == "-o" && (index + 1) < args.Length)
			{
				outputPath = args[++index];
			}
			else if (args[index] == "-E")
			{
				preprocessOnly = true;
			}
		}

		if (!File.Exists(inputPath))
		{
			Console.Error.WriteLine($"File not found: {inputPath}");
			return 1;
		}

		SourceLine[] sourceMap = [];

		try
		{
			sourceMap = SourceLoader.Load(inputPath);
		}
		catch (AssemblyException exception)
		{
			PrintDiagnostic(sourceMap, exception.Line, exception.Column, DiagnosticSeverity.Error, exception.Message);
			return 1;
		}

		if (preprocessOnly)
		{
			foreach (SourceLine sourceLine in sourceMap)
			{
				Console.WriteLine($"{sourceLine.File}:{sourceLine.FileLineNumber,-4} {sourceLine.Text}");
			}
			return 0;
		}

		AssembledOutput result;

		try
		{
			string[] lines = Array.ConvertAll(sourceMap, sourceLine => sourceLine.Text);
			List<Token> tokens = new Lexer(lines).Tokenize();
			List<ParsedStatement> statements = new Parser(tokens).Parse();
			result = new CodeGenerator(statements).Assemble();
		}
		catch (AssemblyException exception)
		{
			PrintDiagnostic(sourceMap, exception.Line, exception.Column, DiagnosticSeverity.Error, exception.Message);
			return 1;
		}

		bool hasErrors = false;
		foreach (Diagnostic diagnostic in result.Diagnostics)
		{
			PrintDiagnostic(sourceMap, diagnostic.Line, diagnostic.Column, diagnostic.Severity, diagnostic.Message);
			if (diagnostic.IsError)
			{
				hasErrors = true;
			}
		}

		if (hasErrors)
		{
			return 1;
		}

		File.WriteAllBytes(outputPath, result.Bytes);
		stopwatch.Stop();

		Console.WriteLine(
			$"Assembled {result.Bytes.Length} bytes into \"{outputPath}\" in {stopwatch.ElapsedMilliseconds / 1000f} seconds"
		);
		Console.WriteLine($"Symbols ({result.SymbolTable.Count}):");
		foreach ((string name, uint address) in result.SymbolTable)
		{
			Console.WriteLine($"\t{name,-20} = {address:X4}h");
		}

		return 0;
	}

	private static void PrintDiagnostic(
		SourceLine[] sourceMap,
		int line,
		int column,
		DiagnosticSeverity severity,
		string message
	)
	{
		string label = severity == DiagnosticSeverity.Error ? "error" : "warning";
		TextWriter output = severity == DiagnosticSeverity.Error ? Console.Error : Console.Out;

		bool lineInRange = line > 0 && line <= sourceMap.Length;
		if (lineInRange)
		{
			SourceLine sourceLine = sourceMap[line - 1];
			output.WriteLine($"{sourceLine.File}:{sourceLine.FileLineNumber}:{column}: {label}: {message}");
			output.WriteLine($" {sourceLine.Text}");

			string prefix = sourceLine.Text.Length >= column ? sourceLine.Text[..column] : "";
			string expanded = ExpandTabs(prefix);
			output.WriteLine($"{expanded}^");
		}
		else
		{
			output.WriteLine($"{label}: {message}");
		}
	}

	private static string ExpandTabs(string text, int tabWidth = 4)
	{
		var builder = new StringBuilder();
		foreach (char character in text)
		{
			if (character == '\t')
			{
				int spaces = tabWidth - (builder.Length % tabWidth);
				builder.Append(' ', spaces);
			}
			else
			{
				builder.Append(' ');
			}
		}
		return builder.ToString();
	}
}
