namespace im8000asm;

internal class ParsedInstruction
{
    public ParsedInstruction(LexedLine line)
    {
        LineNumber = line.LineNumber;

        if (line.Mnemonic is null)
        {
            throw new Exception($"Internal error at {LineNumber}: Called ParsedInstruction with null mnemonic");
        }

        if (!Enum.TryParse(line.Mnemonic, out Constants.Mnemonic mnemonic))
        {
            throw new Exception($"Error at {LineNumber}: Unknown instruction \"{line.Mnemonic}\"");
        }
        Mnemonic = mnemonic;

        OperandSize = line.OperandSize switch
        {
            "B" => Constants.OperandSize.Byte,
            "W" => Constants.OperandSize.Word,
            "D" => Constants.OperandSize.DWord,
            null => Constants.OperandSize.Implied,
            _ => throw new Exception($"Error at {LineNumber}: Unknown size suffix \".{line.OperandSize}\"")
        };

        Operands = [];
        foreach (string operand in line.Operands)
        {
            Operands.Add(OperandParser.Parse(operand));
        }
    }

    public int LineNumber { get; set; }
    public Constants.Mnemonic Mnemonic { get; set; }
    public Constants.OperandSize OperandSize { get; set; }
    public List<Operand> Operands { get; set; }
}
