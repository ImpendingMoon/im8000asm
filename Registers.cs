namespace im8000asm;

public static class Registers
{
	public static readonly IReadOnlyDictionary<string, NarrowRegister> ByNarrowName =
		new Dictionary<string, NarrowRegister>(StringComparer.OrdinalIgnoreCase)
		{
			["A"] = NarrowRegister.A,
			["B"] = NarrowRegister.B,
			["C"] = NarrowRegister.C,
			["D"] = NarrowRegister.D,
			["E"] = NarrowRegister.E,
			["H"] = NarrowRegister.H,
			["L"] = NarrowRegister.L,
		};

	public static readonly IReadOnlyDictionary<string, WideRegister> ByWideName =
		new Dictionary<string, WideRegister>(StringComparer.OrdinalIgnoreCase)
		{
			["AF"] = WideRegister.AF,
			["BC"] = WideRegister.BC,
			["DE"] = WideRegister.DE,
			["HL"] = WideRegister.HL,
			["IX"] = WideRegister.IX,
			["IY"] = WideRegister.IY,
			["SP"] = WideRegister.SP,
		};

	public static bool TryParseNarrow(string name, out NarrowRegister register)
	{
		return ByNarrowName.TryGetValue(name, out register);
	}

	public static bool TryParseWide(string name, out WideRegister register)
	{
		return ByWideName.TryGetValue(name, out register);
	}

	public static bool IsNarrowName(string name)
	{
		return ByNarrowName.ContainsKey(name);
	}

	public static bool IsWideName(string name)
	{
		return ByWideName.ContainsKey(name);
	}

	/// <summary>
	///     IX, IY, and SP always require a displacement when used as memory operands.
	/// </summary>
	public static bool RequiresDisplacement(WideRegister register)
	{
		return register is WideRegister.IX or WideRegister.IY or WideRegister.SP;
	}
}
