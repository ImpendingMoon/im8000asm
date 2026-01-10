using System.Text.RegularExpressions;

namespace im8000asm;

internal class AsmLine
{
    public AsmLine(string rawLine, int lineNumber)
    {
        LineNumber = lineNumber;
        Operands = [];

        string line = rawLine;
        line = StripComments(line);
        line = GetLabel(line);
        line = GetMnemonic(line);
        line = GetOperandSize(line);
        GetOperands(line);
    }

    public int LineNumber { get; set; }
    public string? Label { get; set; }
    public string? Mnemonic { get; set; }
    public string? OperandSize { get; set; }
    public string[] Operands { get; set; }

    public override string ToString()
    {
        return $"[{LineNumber:0000}] {Label} {Mnemonic}{OperandSize} {string.Join(',', Operands)}";
    }

    /// <summary>
    /// Strips unquoted comments from the line
    /// </summary>
    /// <param name="line">Raw assembly line</param>
    /// <returns>Assembly line with comments stripped</returns>
    private string StripComments(string line)
    {
        int splitAt = -1;
        bool inSingleQuotes = false;
        bool inDoubleQuotes = false;
        bool escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (escaped)
            {
                escaped = false;
            }
            else if (c == '\\' && (inSingleQuotes || inDoubleQuotes))
            {
                escaped = true;
            }
            else if (c == '\'')
            {
                inSingleQuotes = !inSingleQuotes;
            }
            else if (c == '\"')
            {
                inDoubleQuotes = !inDoubleQuotes;
            }
            else if (c == ';' && !inSingleQuotes && !inDoubleQuotes)
            {
                splitAt = i;
                break;
            }
        }

        if (splitAt != -1)
        {
            return line.Substring(0, splitAt);
        }

        return line;
    }

    /// <summary>
    /// Gets the label, if any, from the line
    /// </summary>
    /// <param name="line">Assembly line with comments stripped</param>
    /// <returns>Assembly line with label stripped</returns>
    private string GetLabel(string line)
    {
        // Label is any text at the start of the line, ending with a colon
        Match match = Regex.Match(line, @"^[a-zA-Z._][a-zA-Z0-9._]*:");

        if (match.Success)
        {
            Label = match.Value;
            line = line.Substring(match.Length).TrimStart();
        }

        return line;
    }

    /// <summary>
    /// Gets the mnemonic, if any, from the line
    /// </summary>
    /// <param name="line">Assembly line with label stripped</param>
    /// <returns>Assembly line with label stripped</returns>
    private string GetMnemonic(string line)
    {
        // Mnemonic is 1-5 alphabetic characters
        Match match = Regex.Match(line, @"^[a-zA-Z]{1,5}");

        if (match.Success)
        {
            Mnemonic = match.Value;
            line = line.Substring(match.Length).TrimStart();
        }

        return line;
    }

    /// <summary>
    /// Gets the operand size, if any, from the line
    /// </summary>
    /// <param name="line">Assembly line with mnemonic stripped</param>
    /// <returns>Assembly line with size stripped</returns>
    private string GetOperandSize(string line)
    {
        // Mnemonic is 1-5 alphabetic characters
        Match match = Regex.Match(line, @"^\.[BWD]");

        if (match.Success)
        {
            OperandSize = match.Value;
            line = line.Substring(match.Length);
        }

        return line;
    }

    /// <summary>
    /// Gets the operands, if any, from the line
    /// </summary>
    /// <param name="line">Assembly line with size stripped</param>
    private void GetOperands(string line)
    {
        Operands = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int i = 0; i < Operands.Length; i++)
        {
            Operands[i] = Operands[i].ToUpperInvariant().Replace(" ", "");
        }
    }
}
