namespace im8000asm;

using static OperandDescriptors;

public static class IsaTable
{
	public static readonly IReadOnlyDictionary<string, InstructionDefinition> Instructions = BuildTable(
		RAndRm(nameof(Mnemonic.LD), 0x00, 0x00),
		ExInstruction(),
		RAndRm(nameof(Mnemonic.ADD), 0x02, 0x02),
		RAndRm(nameof(Mnemonic.ADC), 0x03, 0x03),
		RAndRm(nameof(Mnemonic.SUB), 0x04, 0x04),
		RAndRm(nameof(Mnemonic.SBC), 0x05, 0x05),
		RAndRm(nameof(Mnemonic.CP), 0x06, 0x06),
		RAndRm(nameof(Mnemonic.AND), 0x07, 0x07),
		RAndRm(nameof(Mnemonic.OR), 0x08, 0x08),
		RAndRm(nameof(Mnemonic.XOR), 0x09, 0x09),
		RAndRm(nameof(Mnemonic.TST), 0x0A, 0x0A),
		RAndRm(nameof(Mnemonic.BIT), 0x0B, 0x0B),
		RAndRm(nameof(Mnemonic.SET), 0x0C, 0x0C),
		RAndRm(nameof(Mnemonic.RES), 0x0D, 0x0D),
		RAndRm(nameof(Mnemonic.RLC), 0x0E, 0x0E),
		RAndRm(nameof(Mnemonic.RRC), 0x0F, 0x0F),
		RAndRm(nameof(Mnemonic.RL), 0x10, 0x10),
		RAndRm(nameof(Mnemonic.RR), 0x11, 0x11),
		RAndRm(nameof(Mnemonic.SLA), 0x12, 0x12),
		RAndRm(nameof(Mnemonic.SRA), 0x13, 0x13),
		RAndRm(nameof(Mnemonic.SRL), 0x14, 0x14),
		In(nameof(Mnemonic.IN), 0x15),
		Out(nameof(Mnemonic.OUT), 0x15),
		ExhInstruction(),
		Ur(nameof(Mnemonic.PUSH), 0x0, 2, WideRegisterOnly),
		Ur(nameof(Mnemonic.POP), 0x0, 3, WideRegisterOnly),
		UrAndUm(nameof(Mnemonic.INC), 0x0, 4),
		UrAndUm(nameof(Mnemonic.DEC), 0x0, 5),
		UrAndUm(nameof(Mnemonic.NEG), 0x0, 6),
		UrAndUm(nameof(Mnemonic.EXT), 0x0, 7),
		UrAndUm(nameof(Mnemonic.MLT), 0x1, 0),
		UrAndUm(nameof(Mnemonic.DIV), 0x1, 1),
		UrAndUm(nameof(Mnemonic.SDIV), 0x1, 2),
		UrAndUm(nameof(Mnemonic.CPL), 0x1, 3),
		Branch(nameof(Mnemonic.JP), 0x00),
		BranchRelative(nameof(Mnemonic.JR), 0x01, 0x02),
		Branch(nameof(Mnemonic.CALL), 0x04),
		BranchRelative(nameof(Mnemonic.CALLR), 0x05, 0x06),
		Ret(nameof(Mnemonic.RET), 0x08),
		Ret(nameof(Mnemonic.RETI), 0x09),
		Ret(nameof(Mnemonic.RETN), 0x0A),
		Blk(nameof(Mnemonic.LDI), 0x0, 0x0, true, false),
		Blk(nameof(Mnemonic.LDIR), 0x0, 0x0, true, true),
		Blk(nameof(Mnemonic.LDD), 0x0, 0x0, false, false),
		Blk(nameof(Mnemonic.LDDR), 0x0, 0x0, false, true),
		Blk(nameof(Mnemonic.CPI), 0x1, 0x0, true, false),
		Blk(nameof(Mnemonic.CPIR), 0x1, 0x0, true, true),
		Blk(nameof(Mnemonic.CPD), 0x1, 0x0, false, false),
		Blk(nameof(Mnemonic.CPDR), 0x1, 0x0, false, true),
		Blk(nameof(Mnemonic.TSI), 0x1, 0x1, true, false),
		Blk(nameof(Mnemonic.TSIR), 0x1, 0x1, true, true),
		Blk(nameof(Mnemonic.TSD), 0x1, 0x1, false, false),
		Blk(nameof(Mnemonic.TSDR), 0x1, 0x1, false, true),
		Blk(nameof(Mnemonic.INI), 0x2, 0x0, true, false),
		Blk(nameof(Mnemonic.INIR), 0x2, 0x0, true, true),
		Blk(nameof(Mnemonic.IND), 0x2, 0x0, false, false),
		Blk(nameof(Mnemonic.INDR), 0x2, 0x0, false, true),
		Blk(nameof(Mnemonic.OUTI), 0x3, 0x0, true, false),
		Blk(nameof(Mnemonic.OTIR), 0x3, 0x0, true, true),
		Blk(nameof(Mnemonic.OUTD), 0x3, 0x0, false, false),
		Blk(nameof(Mnemonic.OTDR), 0x3, 0x0, false, true),
		N(nameof(Mnemonic.EXX), 0x4, 0x00),
		N(nameof(Mnemonic.EXI), 0x4, 0x01),
		NImmediate(nameof(Mnemonic.RST), 0x5, 0x00),
		N(nameof(Mnemonic.SCF), 0x5, 0x01),
		N(nameof(Mnemonic.CCF), 0x5, 0x02),
		N(nameof(Mnemonic.DAA), 0x6, 0x00),
		N(nameof(Mnemonic.RLD), 0x6, 0x01),
		N(nameof(Mnemonic.RRD), 0x6, 0x02),
		N(nameof(Mnemonic.HALT), 0x8, 0x00),
		N(nameof(Mnemonic.EI), 0x8, 0x01),
		N(nameof(Mnemonic.DI), 0x8, 0x02),
		NImmediate(nameof(Mnemonic.IM), 0x8, 0x00),
		LdSpecialInstruction(),
		SB(nameof(Mnemonic.NOP), 0x0),
		SB(nameof(Mnemonic.DJNZ), 0x1, true),
		SB(nameof(Mnemonic.JANZ), 0x2, true)
	);

	private static InstructionVariant RVariant(byte opcode)
	{
		return new InstructionVariant(InstructionFormat.FormatR, opcode, [AnyRegister, AnyRegisterOrImmediate]);
	}

	private static InstructionVariant[] RmVariants(byte opcode)
	{
		return
		[
			new InstructionVariant(InstructionFormat.FormatRm, opcode, [AnyRegister, Memory]),
			new InstructionVariant(InstructionFormat.FormatRm, opcode, [AnyRegister, MemoryOrImmediate]),
			new InstructionVariant(InstructionFormat.FormatRm, opcode, [Memory, AnyRegister]),
			new InstructionVariant(InstructionFormat.FormatRm, opcode, [IndirectOrIndexedImmediate, ImmediateOnly]),
		];
	}

	private static InstructionDefinition RAndRm(string mnemonicName, byte rOpcode, byte rmOpcode)
	{
		return new InstructionDefinition(mnemonicName, [RVariant(rOpcode), ..RmVariants(rmOpcode)]);
	}

	private static InstructionDefinition In(string mnemonicName, byte opcode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[new InstructionVariant(InstructionFormat.FormatRm, opcode, [NarrowRegisterOnly, Memory])]
		);
	}

	private static InstructionDefinition Out(string mnemonicName, byte opcode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[new InstructionVariant(InstructionFormat.FormatRm, opcode, [Memory, NarrowRegisterOnly])]
		);
	}

	private static InstructionDefinition ExInstruction()
	{
		return new InstructionDefinition(
			nameof(Mnemonic.EX),
			[
				RVariant(0x01),
				..RmVariants(0x01),
				new InstructionVariant(InstructionFormat.FormatUr, 0x0, [AnyRegister, AltRegister]),
			]
		);
	}

	private static InstructionDefinition ExhInstruction()
	{
		return new InstructionDefinition(
			nameof(Mnemonic.EXH),
			[
				new InstructionVariant(InstructionFormat.FormatUr, 0x0, [AnyRegister], FunctionCode: 1),
				new InstructionVariant(InstructionFormat.FormatUm, 0x0, [Memory], FunctionCode: 1),
			]
		);
	}

	private static InstructionDefinition LdSpecialInstruction()
	{
		return new InstructionDefinition(
			"LD",
			[
				new InstructionVariant(
					InstructionFormat.FormatN,
					0x8,
					[SpecialRegisterOnly, ImmediateOnly],
					FunctionCode: 0x07
				),
				new InstructionVariant(
					InstructionFormat.FormatN,
					0x8,
					[SpecialRegisterOnly, NarrowRegisterOnly],
					FunctionCode: 0x08
				),
				new InstructionVariant(
					InstructionFormat.FormatN,
					0x8,
					[NarrowRegisterOnly, SpecialRegisterOnly],
					FunctionCode: 0x09
				),
			]
		);
	}

	private static InstructionDefinition Ur(
		string mnemonicName,
		byte opcode,
		byte functionCode,
		OperandDescriptor operand
	)
	{
		return new InstructionDefinition(
			mnemonicName,
			[new InstructionVariant(InstructionFormat.FormatUr, opcode, [operand], FunctionCode: functionCode)]
		);
	}

	private static InstructionDefinition UrAndUm(string mnemonicName, byte opcode, byte functionCode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[
				new InstructionVariant(InstructionFormat.FormatUr, opcode, [AnyRegister], FunctionCode: functionCode),
				new InstructionVariant(InstructionFormat.FormatUm, opcode, [Memory], FunctionCode: functionCode),
			]
		);
	}

	private static InstructionDefinition Branch(string mnemonicName, byte opcode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[
				new InstructionVariant(InstructionFormat.FormatB, opcode, [BranchTarget]),
				new InstructionVariant(InstructionFormat.FormatB, opcode, [Condition, BranchTarget]),
			]
		);
	}

	private static InstructionDefinition BranchRelative(string mnemonicName, byte byteOpcode, byte wordOpcode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[
				new InstructionVariant(InstructionFormat.FormatB, wordOpcode, [BranchTarget]),
				new InstructionVariant(InstructionFormat.FormatB, wordOpcode, [Condition, BranchTarget]),
				new InstructionVariant(InstructionFormat.FormatB, byteOpcode, [BranchTarget], [OperandSize.Byte]),
				new InstructionVariant(
					InstructionFormat.FormatB,
					byteOpcode,
					[Condition, BranchTarget],
					[OperandSize.Byte]
				),
			]
		);
	}

	private static InstructionDefinition Ret(string mnemonicName, byte opcode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[
				new InstructionVariant(InstructionFormat.FormatB, opcode, []),
				new InstructionVariant(InstructionFormat.FormatB, opcode, [Condition]),
			]
		);
	}

	private static InstructionDefinition N(string mnemonicName, byte opcode, byte functionCode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[new InstructionVariant(InstructionFormat.FormatN, opcode, [], FunctionCode: functionCode)]
		);
	}

	private static InstructionDefinition NImmediate(string mnemonicName, byte opcode, byte functionCode)
	{
		return new InstructionDefinition(
			mnemonicName,
			[new InstructionVariant(InstructionFormat.FormatN, opcode, [ImmediateOnly], FunctionCode: functionCode)]
		);
	}

	private static InstructionDefinition Blk(
		string mnemonicName,
		byte opcode,
		byte functionCode,
		bool increment,
		bool repeat
	)
	{
		return new InstructionDefinition(
			mnemonicName,
			[
				new InstructionVariant(
					InstructionFormat.FormatBlk,
					opcode,
					[],
					FunctionCode: functionCode,
					Increment: increment,
					Repeat: repeat
				),
			]
		);
	}

	private static InstructionDefinition SB(string mnemonicName, byte opcode, bool hasDisplacement = false)
	{
		return new InstructionDefinition(
			mnemonicName,
			[new InstructionVariant(InstructionFormat.FormatS, opcode, hasDisplacement ? [BranchTarget] : [])]
		);
	}

	private static Dictionary<string, InstructionDefinition> BuildTable(params InstructionDefinition[] definitions)
	{
		Dictionary<string, InstructionDefinition> table = new(StringComparer.OrdinalIgnoreCase);

		foreach (InstructionDefinition definition in definitions)
		{
			if (table.TryGetValue(definition.MnemonicName, out InstructionDefinition? existing))
			{
				table[definition.MnemonicName] = definition with
				{
					Variants = [..existing.Variants, ..definition.Variants],
				};
			}
			else
			{
				table[definition.MnemonicName] = definition;
			}
		}

		return table;
	}
}
