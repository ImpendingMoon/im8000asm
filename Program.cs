namespace im8000asm;

internal class Program
{
    private static void Main(string[] args)
    {
        string[] source =
        [
            "\tLD.B L, 0\t;a",
            "\tLD.B H, 1\t;b",
            "",
            "\tLD.W B, 23\t;counter",
            "fib_loop:",
            "\tLD.W A, L",
            "\tADD.W A, H",
            "\tLD.W L, H",
            "\tLD.W H, A",
            "\tDJNZ fib_loop",
            "",
            "\tJR.W $",
            "\tLD A, (0x1234ABCD)",
            "\tADD (IX+2), B",
            "\tLD (label+2), HL",
            "\tLDIR",
        ];

        var lexed = new List<LexedLine>();

        for (int i = 0; i < source.Length; i++)
        {
            var asmLine = new LexedLine(source[i], i + 1);
            lexed.Add(asmLine);
        }

        foreach (LexedLine asmLine in lexed)
        {
            Console.WriteLine(asmLine);

            if (asmLine.Mnemonic is not null)
            {
                var instruction = new ParsedInstruction(asmLine);
            }
        }
    }
}
