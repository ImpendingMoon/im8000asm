namespace im8000asm;

internal class Program
{
    private static void Main(string[] args)
    {
        string asmLine = "test: LD.W A, 0 ; comment :)";
        var parsed = new AsmLine(asmLine, 1);
    }
}
