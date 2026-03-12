namespace im8000asm;

public static class OperandDescriptors
{
	public static readonly OperandDescriptor AnyRegister = Modes(
		AddressingMode.NarrowRegister,
		AddressingMode.WideRegister
	);

	public static readonly OperandDescriptor AnyRegisterOrImmediate = Modes(
		AddressingMode.NarrowRegister,
		AddressingMode.WideRegister,
		AddressingMode.Immediate
	);

	public static readonly OperandDescriptor AltRegister = Modes(
		AddressingMode.AltNarrowRegister,
		AddressingMode.AltWideRegister
	);

	public static readonly OperandDescriptor Memory = Modes(
		AddressingMode.Indirect,
		AddressingMode.Indexed,
		AddressingMode.DirectMemory
	);

	public static readonly OperandDescriptor MemoryOrImmediate = Modes(
		AddressingMode.Indirect,
		AddressingMode.Indexed,
		AddressingMode.DirectMemory,
		AddressingMode.Immediate
	);

	public static readonly OperandDescriptor NarrowRegisterOnly = Modes(AddressingMode.NarrowRegister);

	public static readonly OperandDescriptor WideRegisterOnly = Modes(AddressingMode.WideRegister);

	public static readonly OperandDescriptor ImmediateOnly = Modes(AddressingMode.Immediate);

	public static readonly OperandDescriptor Condition = Modes(AddressingMode.Condition);

	public static readonly OperandDescriptor SpecialRegisterOnly = Modes(AddressingMode.SpecialRegister);

	public static readonly OperandDescriptor BranchTarget = Modes(
		AddressingMode.NarrowRegister,
		AddressingMode.WideRegister,
		AddressingMode.Indirect,
		AddressingMode.Indexed,
		AddressingMode.DirectMemory,
		AddressingMode.Immediate
	);

	private static OperandDescriptor Modes(params AddressingMode[] modes)
	{
		return new OperandDescriptor(modes);
	}
}
