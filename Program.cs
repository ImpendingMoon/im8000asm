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
			Console.Error.WriteLine(
				"Usage: assembler <input.asm> [-o <output.bin>] [-l [<output.lst>]] [-s [<output.sym>]] [-E]"
			);
			return 1;
		}

		string inputPath = args[0];
		string outputPath = "a.bin";
		string? listingPath = null;
		string? symbolPath = null;
		bool preprocessOnly = false;

		for (int index = 1; index < args.Length; index++)
		{
			if (args[index] == "-o" && (index + 1) < args.Length)
			{
				outputPath = args[++index];
			}
			else if (args[index] == "-l")
			{
				listingPath = (index + 1) < args.Length && !args[index + 1].StartsWith('-')
					? args[++index]
					: Path.ChangeExtension(outputPath, ".lst");
			}
			else if (args[index] == "-s")
			{
				symbolPath = (index + 1) < args.Length && !args[index + 1].StartsWith('-')
					? args[++index]
					: Path.ChangeExtension(outputPath, ".sym");
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

		stopwatch.Stop();

		if (!TryWriteAllBytes(outputPath, result.Bytes))
		{
			return 1;
		}

		Console.WriteLine(
			$"Assembled {result.Bytes.Length} bytes into \"{outputPath}\" in {stopwatch.ElapsedMilliseconds / 1000f} seconds"
		);

		if (listingPath is not null && TryWriteAllText(listingPath, BuildListingText(sourceMap, result)))
		{
			Console.WriteLine($"Listing written to \"{listingPath}\"");
		}

		if (symbolPath is not null && TryWriteAllText(symbolPath, BuildSymbolText(result.SymbolTable)))
		{
			Console.WriteLine($"Symbol table written to \"{symbolPath}\"");
		}

		return 0;
	}

	private static bool TryWriteAllBytes(string path, byte[] bytes)
	{
		try
		{
			File.WriteAllBytes(path, bytes);
			return true;
		}
		catch (IOException ex)
		{
			Console.Error.WriteLine($"error: {ex.Message}");
			return false;
		}
	}

	private static bool TryWriteAllText(string path, string text)
	{
		try
		{
			File.WriteAllText(path, text);
			return true;
		}
		catch (IOException ex)
		{
			Console.Error.WriteLine($"error: {ex.Message}");
			return false;
		}
	}

	private static string BuildListingText(SourceLine[] sourceMap, AssembledOutput result)
	{
		Dictionary<int, ListingRecord> byLine = new();
		foreach (ListingRecord record in result.Listing)
		{
			byLine[record.SourceLine] = record;
		}

		const int bytesPerRow = 4;
		var sb = new StringBuilder();

		for (int lineIndex = 0; lineIndex < sourceMap.Length; lineIndex++)
		{
			int lineNumber = lineIndex + 1;
			SourceLine sourceLine = sourceMap[lineIndex];

			if (!byLine.TryGetValue(lineNumber, out ListingRecord? record) || record.ByteCount == 0)
			{
				sb.AppendLine($"              {lineNumber,5}  {sourceLine.Text}");
				continue;
			}

			ReadOnlySpan<byte> allBytes = result.Bytes.AsSpan(record.ByteOffset, record.ByteCount);
			int rows = ((record.ByteCount + bytesPerRow) - 1) / bytesPerRow;

			for (int row = 0; row < rows; row++)
			{
				int rowOffset = row * bytesPerRow;
				ReadOnlySpan<byte> rowBytes = allBytes.Slice(
					rowOffset,
					Math.Min(bytesPerRow, record.ByteCount - rowOffset)
				);

				uint rowAddress = record.Address + (uint)rowOffset;
				string addrStr = $"{rowAddress:X8}";
				string addrFormatted = addrStr[..4] + "_" + addrStr[4..];

				string hex = string.Join(" ", rowBytes.ToArray().Select(b => $"{b:X2}"));
				string hexPadded = hex.PadRight((bytesPerRow * 3) - 1);

				if (row == 0)
				{
					sb.AppendLine($"{addrFormatted}  {hexPadded}  {lineNumber,5}  {sourceLine.Text}");
				}
				else
				{
					sb.AppendLine($"{addrFormatted}  {hexPadded}");
				}
			}
		}

		return sb.ToString();
	}

	private static string BuildSymbolText(IReadOnlyDictionary<string, long> symbolTable)
	{
		var sb = new StringBuilder();
		foreach ((string name, long value) in symbolTable.OrderBy(kv => kv.Value))
		{
			string formatted = value is >= 0 and <= 0xFFFFFFFFL
				? $"{(uint)value:X8}"[..4] + "_" + $"{(uint)value:X8}"[4..]
				: value.ToString();
			sb.AppendLine($"{formatted}  {name}");
		}

		return sb.ToString();
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

		if (line > 0 && line <= sourceMap.Length)
		{
			SourceLine sourceLine = sourceMap[line - 1];
			output.WriteLine($"{sourceLine.File}:{sourceLine.FileLineNumber}:{column}: {label}: {message}");
		}
		else
		{
			output.WriteLine($"{label}: {message}");
		}
	}
}
