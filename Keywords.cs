namespace im8000asm;

public static class Keywords
{
	// Size-suffix keywords that are reserved and cannot be used as labels.
	public static readonly IReadOnlySet<string> SizeSuffixNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"B",
		"W",
		"D",
	};

	// Directives whose operand lists may contain quoted string literals.
	public static readonly IReadOnlySet<Directive> StringAcceptingDirectives = new HashSet<Directive>
	{
		Directive.DB,
		Directive.INCBIN,
	};

	public static readonly IReadOnlySet<Mnemonic> BranchMnemonics = new HashSet<Mnemonic>
	{
		Mnemonic.JP,
		Mnemonic.JR,
		Mnemonic.CALL,
		Mnemonic.CALLR,
		Mnemonic.RET,
		Mnemonic.RETI,
		Mnemonic.RETN,
	};

	// Maps interrupt mode number to IM function code byte.
	private static readonly Dictionary<long, byte> InterruptModeFunctionCodes = new()
	{
		[1] = 0x05,
		[2] = 0x06,
	};

	public static bool TryParseDirective(string name, out Directive directive)
	{
		return Enum.TryParse(name, true, out directive);
	}

	public static bool TryParseMnemonic(string name, out Mnemonic mnemonic)
	{
		return Enum.TryParse(name, true, out mnemonic);
	}

	public static bool TryGetInterruptModeFunctionCode(long interruptMode, out byte functionCode)
	{
		return InterruptModeFunctionCodes.TryGetValue(interruptMode, out functionCode);
	}
}
