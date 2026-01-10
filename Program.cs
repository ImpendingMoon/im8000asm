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
            "JR.W $"
        ];

        var parsed = new List<AsmLine>();

        for (int i = 0; i < source.Length; i++)
        {
            var asmLine = new AsmLine(source[i], i + 1);
            parsed.Add(asmLine);
        }

        foreach (AsmLine asmLine in parsed)
        {
            Console.WriteLine(asmLine);
        }
    }
}
