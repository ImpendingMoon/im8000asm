namespace im8000asm;

public class AssemblyException : Exception
{
	public AssemblyException(int line, int column, string message) : base($"[{line}:{column}] {message}")
	{
		Line = line;
		Column = column;
		Message = message;
	}

	public int Line { get; }
	public int Column { get; }
	public new string Message { get; }
}

public enum DiagnosticSeverity
{
	Warning,
	Error,
}

public record Diagnostic(int Line, int Column, DiagnosticSeverity Severity, string Message)
{
	public bool IsError => Severity == DiagnosticSeverity.Error;
}
