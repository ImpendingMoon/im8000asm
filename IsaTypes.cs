namespace im8000asm;

public enum OperandSize
{
	Byte = 0b00,
	Word = 0b01,
	Dword = 0b10,
}

public enum NarrowRegister
{
	A = 0b000,
	B = 0b001,
	C = 0b010,
	D = 0b011,
	E = 0b100,
	H = 0b101,
	L = 0b110,
	Immediate = 0b111,
}

public enum WideRegister
{
	AF = 0b000,
	BC = 0b001,
	DE = 0b010,
	HL = 0b011,
	IX = 0b100,
	IY = 0b101,
	SP = 0b110,
	DirectOrImmediate = 0b111,
}

public enum AddressingMode
{
	NarrowRegister,
	WideRegister,
	AltNarrowRegister,
	AltWideRegister,
	Immediate,
	Indirect,
	Indexed,
	DirectMemory,
	Condition,
	SpecialRegister,
}

// These names must match the condition code keywords used in assembly source, e.g. "NZ", "Z", "NC".
public enum BranchCondition : byte
{
	Nz = 0b0000,
	Z = 0b0001,
	Nc = 0b0010,
	C = 0b0011,
	Po = 0b0100,
	Pe = 0b0101,
	P = 0b0110,
	M = 0b0111,
	Always = 0b1111,
}

public enum SpecialRegister
{
	I,
	R,
}

public enum Directive
{
	ORG,
	DB,
	DW,
	DD,
	EQU,
	DS,
	DEFS,
	INCLUDE,
	INCBIN,
	ALIGN,
}

public enum Mnemonic
{
	LD, EX, ADD, ADC, SUB, SBC, CP, AND, OR, XOR,
	TST, BIT, SET, RES, RLC, RRC, RL, RR, SLA, SRA, SRL,
	IN, OUT,

	EXH, PUSH, POP, INC, DEC, NEG, EXT,
	MLT, DIV, SDIV, CPL,

	JP, JR, CALL, CALLR, RET, RETI, RETN,

	LDI, LDIR, LDD, LDDR,
	CPI, CPIR, CPD, CPDR,
	TSI, TSIR, TSD, TSDR,
	INI, INIR, IND, INDR,
	OUTI, OTIR, OUTD, OTDR,
	EXX, EXI,
	RST, SCF, CCF,
	DAA, RLD, RRD,
	HALT, EI, DI, IM,

	NOP, DJNZ, JANZ,
}

public enum InstructionFormat
{
	FormatR,
	FormatRm,
	FormatUr,
	FormatUm,
	FormatB,
	FormatN,
	FormatS,
	FormatBlk,
}

public sealed record InstructionVariant(
	InstructionFormat Format,
	byte Opcode,
	OperandDescriptor[] Operands,
	OperandSize[]? RequiredSizes = null,
	byte FunctionCode = 0,
	bool Increment = false,
	bool Repeat = false
);

public sealed record InstructionDefinition(string MnemonicName, InstructionVariant[] Variants);

public sealed record OperandDescriptor(AddressingMode[] AllowedModes)
{
	public bool Allows(AddressingMode mode)
	{
		return AllowedModes.Contains(mode);
	}
}
