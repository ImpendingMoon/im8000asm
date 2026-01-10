using System.Text;
using System.Text.RegularExpressions;

namespace im8000asm;

internal class AsmLine
{
    public AsmLine(string rawLine, int lineNumber)
    {
        HasContent = true;
        LineNumber = lineNumber;
        Operands = [];

        string line = rawLine;
        line = StripComments(line);
        if (string.IsNullOrWhiteSpace(line))
        {
            HasContent = false;
            return;
        }

        line = GetLabel(line);
        line = GetMnemonic(line);
        line = GetOperandSize(line);
        GetOperands(line);
    }

    public bool HasContent { get; set; }
    public int LineNumber { get; set; }
    public string? Label { get; set; }
    public Constants.Mnemonic? Mnemonic { get; set; }
    public Constants.OperandSize? OperandSize { get; set; }
    public string[] Operands { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append(LineNumber.ToString("0000"));
        sb.Append(' ');

        if (Label is not null)
        {
            sb.Append(Label);
            sb.Append(": ");
        }
        else
        {
            sb.Append('\t');
        }

        if (Mnemonic is not null)
        {
            sb.Append(Mnemonic);
            if (OperandSize is not null && OperandSize != Constants.OperandSize.Implied)
            {
                sb.Append('.');
                sb.Append(OperandSize.ToString().AsSpan(0, 1));
            }
            sb.Append(' ');

            sb.Append(string.Join(',', Operands));
        }

        return sb.ToString();
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
            Label = match.Value.Substring(0, match.Value.Length - 1);
            line = line.Substring(match.Length);
        }

        return line.TrimStart();
    }

    /// <summary>
    /// Gets the mnemonic, if any, from the line
    /// </summary>
    /// <param name="line">Assembly line with label stripped</param>
    /// <returns>Assembly line with label stripped</returns>
    private string GetMnemonic(string line)
    {
        // Mnemonic is 1-5 alphabetic characters
        // Pseudo-instruction mnemonics may have a leading period
        Match match = Regex.Match(line, @"^\.?[a-zA-Z]{1,5}");

        if (match.Success)
        {
            string mnemonic = match.Value.ToUpperInvariant();
            if (mnemonic[0] == '.')
            {
                mnemonic = mnemonic.Substring(1);
            }

            if (!Enum.TryParse(mnemonic, out Constants.Mnemonic result))
            {
                throw new Exception($"Unknown mnemonic: {mnemonic}");
            }

            Mnemonic = result;

            line = line.Substring(match.Length);
        }

        return line.TrimStart();
    }

    /// <summary>
    /// Gets the operand size, if any, from the line
    /// </summary>
    /// <param name="line">Assembly line with mnemonic stripped</param>
    /// <returns>Assembly line with size stripped</returns>
    private string GetOperandSize(string line)
    {
        // Operand size is a period followed by B, W, or D
        // Match wider to fail early and loud
        Match match = Regex.Match(line, @"^\.[a-zA-Z]");

        if (match.Success)
        {
            string size = match.Value.Substring(1).ToUpperInvariant();

            OperandSize = size switch
            {
                "B" => Constants.OperandSize.Byte,
                "W" => Constants.OperandSize.Word,
                "D" => Constants.OperandSize.DWord,
                _ => throw new Exception($"Unknown size: {size}")
            };

            line = line.Substring(match.Length);
        }
        else if (Mnemonic is not null)
        {
            OperandSize = Constants.OperandSize.Implied;
        }

        return line.TrimStart();
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
