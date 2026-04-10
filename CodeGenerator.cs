namespace im8000asm;

public record ListingRecord(uint Address, int ByteOffset, int ByteCount, int SourceLine);

public record AssembledOutput(
	byte[] Bytes,
	IReadOnlyDictionary<string, long> SymbolTable,
	IReadOnlyList<Diagnostic> Diagnostics,
	IReadOnlyList<ListingRecord> Listing
);

public record MemoryResolution(byte RegisterCode, long Immediate, OperandSize? ImmediateSize)
{
	public bool HasImmediate => ImmediateSize is not null;
}

public record BranchTargetResolution(byte AddressCode, long AppendedValue, bool HasAppended);

public class CodeGenerator
{
	private const ushort RBaseBits = 0b00;
	private const ushort RMBaseBits = 0b01;
	private const ushort URBaseBits = 0b0010;
	private const ushort UMBaseBits = 0b0110;
	private const ushort BBaseBits = 0b1010;
	private const ushort NBaseBits = 0b1110;
	private const ushort BlkBaseBits = 0b1011;
	private const byte SBBaseBits = 0b1111;

	private static readonly HashSet<string> ReservedNames = BuildReservedNames();

	private readonly List<Diagnostic> _diagnostics = [];
	private readonly List<ListingRecord> _listing = [];
	private readonly List<byte> _output = [];
	private readonly List<ParsedStatement> _statements;
	private readonly Dictionary<string, long> _symbols = new();

	private uint _locationCounter;

	public CodeGenerator(List<ParsedStatement> statements)
	{
		_statements = statements;
	}

	public AssembledOutput Assemble()
	{
		PassOne();

		try
		{
			PassTwo();
		}
		catch (AssemblyException ex)
		{
			_diagnostics.Add(new Diagnostic(ex.Line, ex.Column, DiagnosticSeverity.Error, ex.Message));
		}

		return new AssembledOutput(_output.ToArray(), _symbols, _diagnostics, _listing);
	}

	private void PassOne()
	{
		_locationCounter = 0;

		foreach (ParsedStatement statement in _statements)
		{
			switch (statement)
			{
				case LabelStatement label:
					RegisterLabel(label);
					break;

				case DirectiveStatement directive:
					AdvanceLcForDirective(directive);
					break;

				case InstructionStatement instruction:
					_locationCounter += (uint)MeasureInstruction(instruction);
					break;
			}
		}
	}

	private void RegisterLabel(LabelStatement label)
	{
		if (ReservedNames.Contains(label.Name))
		{
			throw new AssemblyException(
				label.Line,
				label.Column,
				$"'{label.Name}' is a reserved name and cannot be used as a label"
			);
		}

		if (!_symbols.TryAdd(label.Name, _locationCounter))
		{
			throw new AssemblyException(label.Line, label.Column, $"Duplicate label '{label.Name}'");
		}
	}

	private void AdvanceLcForDirective(DirectiveStatement directive)
	{
		switch (directive.Directive)
		{
			case Directive.ORG:
				if (directive.Operands.Length == 1)
				{
					_locationCounter = (uint)EvaluateOperand(directive.Operands[0]);
				}
				break;

			case Directive.DB:
				foreach (ParsedOperand operand in directive.Operands)
				{
					_locationCounter += operand is StringLiteralOperand s ? (uint)s.Value.Length : 1u;
				}
				break;

			case Directive.DW:
				_locationCounter += (uint)directive.Operands.Length * 2;
				break;

			case Directive.DD:
				_locationCounter += (uint)directive.Operands.Length * 4;
				break;

			case Directive.DS:
			case Directive.DEFS:
				if (directive.Operands.Length >= 1)
				{
					_locationCounter += (uint)EvaluateOperand(directive.Operands[0]);
				}
				break;

			case Directive.ALIGN:
				if (directive.Operands.Length >= 1)
				{
					long alignment = EvaluateOperand(directive.Operands[0]);
					if (alignment < 1)
					{
						throw new AssemblyException(
							directive.Line,
							directive.Column,
							$"ALIGN: alignment value must be at least 1, got {alignment}"
						);
					}
					_locationCounter += AlignmentPadding(_locationCounter, alignment);
				}
				break;

			case Directive.RB:
			case Directive.RS:
				if (directive.Operands.Length >= 1)
				{
					_locationCounter += (uint)EvaluateOperand(directive.Operands[0]);
				}
				break;

			case Directive.RW:
				if (directive.Operands.Length >= 1)
				{
					_locationCounter += (uint)EvaluateOperand(directive.Operands[0]) * 2;
				}
				break;

			case Directive.RD:
				if (directive.Operands.Length >= 1)
				{
					_locationCounter += (uint)EvaluateOperand(directive.Operands[0]) * 4;
				}
				break;

			case Directive.EQU:
				HandleEquDirective(directive);
				break;

			case Directive.INCBIN:
				if (TryGetIncbinPath(directive, out string path))
				{
					_locationCounter += (uint)new FileInfo(path).Length;
				}
				break;
		}
	}

	private void HandleEquDirective(DirectiveStatement directive)
	{
		if (!directive.HasLabel)
		{
			Warn(directive.Line, directive.Column, ".EQU has no label - directive ignored");
			return;
		}

		if (directive.Operands.Length == 0)
		{
			Warn(
				directive.Line,
				directive.Column,
				$".EQU for '{directive.LabelName}' has no value - directive ignored"
			);
			return;
		}

		_symbols[directive.LabelName] = EvaluateOperand(directive.Operands[0]);
	}

	private void PassTwo()
	{
		_locationCounter = 0;

		foreach (ParsedStatement statement in _statements)
		{
			uint addressBefore = _locationCounter;
			int offsetBefore = _output.Count;

			switch (statement)
			{
				case DirectiveStatement directive:
					EmitDirective(directive);
					break;

				case InstructionStatement instruction:
					EmitInstruction(instruction);
					break;
			}

			_listing.Add(new ListingRecord(addressBefore, offsetBefore, _output.Count - offsetBefore, statement.Line));
		}
	}

	private static InstructionDefinition LookupDefinition(InstructionStatement instruction)
	{
		if (!IsaTable.Instructions.TryGetValue(instruction.Mnemonic.ToString(), out InstructionDefinition? def))
		{
			throw new AssemblyException(
				instruction.Line,
				instruction.Column,
				$"Unknown mnemonic '{instruction.Mnemonic}'"
			);
		}

		return def;
	}

	private static int MeasureInstruction(InstructionStatement instruction)
	{
		InstructionDefinition definition = LookupDefinition(instruction);
		InstructionVariant? variant = MatchVariant(definition, instruction);
		if (variant is null)
		{
			return 0;
		}

		ValidateNoMixedRegisterSize(instruction);
		return InstructionWordSize(variant) + AppendedByteCount(variant, instruction);
	}

	private static void ValidateNoMixedRegisterSize(InstructionStatement instruction)
	{
		// LEA intentionally mixes a wide destination with a narrow source.
		if (instruction.Mnemonic == Mnemonic.LEA)
		{
			return;
		}

		if (instruction.Operands.Any(IsNarrowRegisterOperand) && instruction.Operands.Any(IsWideRegisterOperand))
		{
			throw new AssemblyException(instruction.Line, 0, $"'{instruction.Mnemonic}': cannot mix register widths");
		}
	}

	private void EmitInstruction(InstructionStatement instruction)
	{
		InstructionDefinition definition = LookupDefinition(instruction);
		InstructionVariant variant = MatchVariant(definition, instruction) ??
			throw new AssemblyException(
				instruction.Line,
				instruction.Column,
				$"Invalid addressing mode for '{instruction.Mnemonic}'"
			);

		ValidateNoMixedRegisterSize(instruction);

		switch (variant.Format)
		{
			case InstructionFormat.FormatR: EmitFormatR(variant, instruction); break;
			case InstructionFormat.FormatRm: EmitFormatRm(variant, instruction); break;
			case InstructionFormat.FormatUr: EmitFormatUr(variant, instruction); break;
			case InstructionFormat.FormatUm: EmitFormatUm(variant, instruction); break;
			case InstructionFormat.FormatB: EmitFormatB(variant, instruction); break;
			case InstructionFormat.FormatN: EmitFormatN(variant, instruction); break;
			case InstructionFormat.FormatS: EmitFormatS(variant, instruction); break;
			case InstructionFormat.FormatBlk: EmitFormatBlk(variant, instruction); break;
			default:
				throw new AssemblyException(
					instruction.Line,
					instruction.Column,
					$"Unimplemented format {variant.Format}"
				);
		}
	}

	private static OperandSize ResolveSize(InstructionStatement instruction)
	{
		bool hasWideRegisterOperand = instruction.Operands.Any(IsWideRegisterOperand);

		if (instruction.Size is not null)
		{
			if (hasWideRegisterOperand && instruction.Size != OperandSize.Dword)
			{
				throw new AssemblyException(
					instruction.Line,
					0,
					$"'{instruction.Mnemonic}': wide register operand is invalid for byte and word operations"
				);
			}

			return instruction.Size.Value;
		}

		return hasWideRegisterOperand ? OperandSize.Dword : OperandSize.Word;
	}

	// For LEA, the two-bit size field encodes the scale rather than a data width.
	private byte ResolveLeaScale(InstructionStatement instruction, uint instructionAddress)
	{
		if (instruction.Operands.Length < 3)
		{
			throw new AssemblyException(instruction.Line, instruction.Column, "LEA requires a scale operand");
		}

		long scale = EvaluateOperand(instruction.Operands[2], instructionAddress);
		return scale switch
		{
			1 => 0b00,
			2 => 0b01,
			4 => 0b10,
			8 => 0b11,
			_ => throw new AssemblyException(
				instruction.Line,
				instruction.Column,
				$"LEA scale must be 1, 2, 4, or 8; got {scale}"
			),
		};
	}

	private void EmitFormatR(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = _locationCounter;
		bool isLea = instruction.Mnemonic == Mnemonic.LEA;

		// For LEA the size field encodes the scale, not a data width.
		byte sizeBits = isLea ? ResolveLeaScale(instruction, instructionAddress) : (byte)ResolveSize(instruction);

		ParsedOperand? destination = instruction.Operands.Length > 0 ? instruction.Operands[0] : null;
		// For LEA operand[2] is the scale (already consumed above); operand[1] is the source.
		ParsedOperand? source = instruction.Operands.Length > 1 ? instruction.Operands[1] : null;

		byte destinationCode = ResolveRegisterCode(destination, instruction.Line, instruction.Column);
		byte sourceCode = ResolveRegisterCodeOrImmediate(
			source,
			out long immediateValue,
			instruction.Line,
			instruction.Column,
			instructionAddress
		);

		ushort word = RBaseBits;
		word |= (ushort)(variant.Opcode << 2);
		word |= (ushort)(sizeBits << 8);
		word |= (ushort)(destinationCode << 10);
		word |= (ushort)(sourceCode << 13);

		EmitWord(word);

		if (sourceCode == (byte)NarrowRegister.Immediate)
		{
			EmitImmediate(
				immediateValue,
				isLea ? OperandSize.Word : ResolveSize(instruction),
				instruction.Line,
				instruction.Column
			);
		}
	}

	private void EmitFormatRm(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = _locationCounter;
		bool isLea = instruction.Mnemonic == Mnemonic.LEA;

		// For LEA: operand[0]=wide dest register, operand[1]=memory source, operand[2]=scale.
		// For all other RM instructions: either operand[0] or operand[1] is memory.
		bool destinationIsMemory = !isLea && IsMemoryOperand(instruction.Operands[0]);
		byte direction = destinationIsMemory ? (byte)0 : (byte)1;
		ParsedOperand memoryOperand = isLea
			? instruction.Operands[1]
			: destinationIsMemory
				? instruction.Operands[0]
				: instruction.Operands[1];
		ParsedOperand registerOperand = isLea
			? instruction.Operands[0]
			: destinationIsMemory
				? instruction.Operands[1]
				: instruction.Operands[0];

		long immediateValue = 0;
		bool registerSideIsImmediate = !isLea && IsImmediateOperand(registerOperand);
		byte registerCode;

		if (registerSideIsImmediate)
		{
			immediateValue = EvaluateOperand(registerOperand, instructionAddress);
			registerCode = (byte)NarrowRegister.Immediate;
		}
		else
		{
			registerCode = ResolveRegisterCode(registerOperand, instruction.Line, instruction.Column);
		}

		MemoryResolution memory = ResolveMemoryOperand(
			memoryOperand,
			instruction.Line,
			instruction.Column,
			instructionAddress
		);

		// For LEA the size field encodes the scale; for others it encodes the data width.
		byte sizeBits = isLea ? ResolveLeaScale(instruction, instructionAddress) : (byte)ResolveSize(instruction);

		ushort word = RMBaseBits;
		word |= (ushort)(variant.Opcode << 2);
		word |= (ushort)(direction << 7);
		word |= (ushort)(sizeBits << 8);
		word |= (ushort)(registerCode << 10);
		word |= (ushort)(memory.RegisterCode << 13);

		EmitWord(word);

		if (memory.HasImmediate)
		{
			EmitImmediate(memory.Immediate, memory.ImmediateSize!.Value, instruction.Line, instruction.Column);
		}

		if (registerCode == (byte)NarrowRegister.Immediate)
		{
			EmitImmediate(immediateValue, ResolveSize(instruction), instruction.Line, instruction.Column);
		}
	}

	private void EmitFormatUr(InstructionVariant variant, InstructionStatement instruction)
	{
		OperandSize size = ResolveSize(instruction);
		byte registerCode = 0b000;

		if (instruction.Operands.Length >= 1)
		{
			registerCode = ResolveRegisterCode(instruction.Operands[0], instruction.Line, instruction.Column);
		}

		if (instruction.Operands.Length == 2)
		{
			byte alternateCode = instruction.Operands[1] switch
			{
				AltNarrowRegisterOperand alt => (byte)alt.Register,
				AltWideRegisterOperand alt => (byte)alt.Register,
				_ => throw new AssemblyException(
					instruction.Line,
					instruction.Column,
					"EX r, r' requires an alternate-register operand"
				),
			};

			if (alternateCode != registerCode)
			{
				throw new AssemblyException(
					instruction.Line,
					instruction.Column,
					"EX r, r' must use the same register on both sides"
				);
			}
		}

		ushort word = URBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)((int)size << 8);
		word |= (ushort)(registerCode << 10);
		word |= (ushort)(variant.FunctionCode << 13);

		EmitWord(word);
	}

	private void EmitFormatUm(InstructionVariant variant, InstructionStatement instruction)
	{
		OperandSize size = ResolveSize(instruction);
		MemoryResolution memory = ResolveMemoryOperand(
			instruction.Operands[0],
			instruction.Line,
			instruction.Column,
			_locationCounter
		);

		ushort word = UMBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)((int)size << 8);
		word |= (ushort)(variant.FunctionCode << 10);
		word |= (ushort)(memory.RegisterCode << 13);

		EmitWord(word);

		if (memory.HasImmediate)
		{
			EmitImmediate(memory.Immediate, memory.ImmediateSize!.Value, instruction.Line, instruction.Column);
		}
	}

	private void EmitFormatB(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = _locationCounter;
		byte conditionCode = (byte)BranchCondition.Always;
		int targetIndex = 0;

		if (instruction.Operands.Length > 0 && instruction.Operands[0] is ConditionOperand conditionOperand)
		{
			conditionCode = (byte)conditionOperand.Condition;
			targetIndex = 1;
		}

		bool isRelative = instruction.Mnemonic is Mnemonic.JR or Mnemonic.CALLR;
		BranchTargetResolution? resolution = null;

		if (targetIndex < instruction.Operands.Length)
		{
			ParsedOperand target = instruction.Operands[targetIndex];
			resolution = isRelative
				? ResolveRelativeBranchTarget(target, instruction, instructionAddress)
				: ResolveAbsoluteBranchTarget(target, instruction.Line, instruction.Column, instructionAddress);
		}

		ushort word = BBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)(conditionCode << 9);
		word |= (ushort)((resolution?.AddressCode ?? 0) << 13);

		EmitWord(word);

		if (resolution?.HasAppended == true)
		{
			OperandSize appendSize = isRelative
				? instruction.Size == OperandSize.Byte ? OperandSize.Byte : OperandSize.Word
				: OperandSize.Dword;
			EmitImmediate(resolution.AppendedValue, appendSize, instruction.Line, instruction.Column);
		}
	}

	private void EmitFormatN(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = _locationCounter;
		byte functionCode = instruction.Mnemonic == Mnemonic.IM
			? ResolveInterruptModeFunctionCode(instruction, instructionAddress)
			: variant.FunctionCode;

		ushort word = NBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)(functionCode << 8);

		EmitWord(word);

		bool isLoadIRegister = instruction.Mnemonic == Mnemonic.LD &&
			instruction.Operands.Length == 2 &&
			instruction.Operands[0] is SpecialRegisterOperand { Register: SpecialRegister.I } &&
			instruction.Operands[1] is ImmediateOrRegisterOperand;

		if (instruction.Mnemonic == Mnemonic.RST)
		{
			EmitImmediate(
				EvaluateOperand(instruction.Operands[0], instructionAddress),
				OperandSize.Byte,
				instruction.Line,
				instruction.Column
			);
		}
		else if (isLoadIRegister)
		{
			EmitImmediate(
				EvaluateOperand(instruction.Operands[1], instructionAddress),
				OperandSize.Dword,
				instruction.Line,
				instruction.Column
			);
		}
	}

	private void EmitFormatS(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = _locationCounter;

		byte word = SBBaseBits;
		word |= (byte)(variant.Opcode << 4);
		EmitByte(word);

		if (instruction.Operands.Length == 1)
		{
			long absoluteTarget = EvaluateOperand(instruction.Operands[0], instructionAddress);
			long displacement = absoluteTarget - (instructionAddress + 2);
			EmitImmediate(displacement, OperandSize.Byte, instruction.Line, instruction.Column);
		}
	}

	private void EmitFormatBlk(InstructionVariant variant, InstructionStatement instruction)
	{
		OperandSize size = ResolveSize(instruction);

		if (size == OperandSize.Dword)
		{
			throw new AssemblyException(
				instruction.Line,
				instruction.Column,
				$"'{instruction.Mnemonic}': block instructions support only byte (.B) and word (.W) sizes"
			);
		}

		ushort word = BlkBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)((int)size << 8);
		word |= (ushort)((variant.Increment ? 1 : 0) << 10);
		word |= (ushort)((variant.Repeat ? 1 : 0) << 11);
		word |= (ushort)(variant.FunctionCode << 12);

		EmitWord(word);
	}

	private BranchTargetResolution ResolveRelativeBranchTarget(
		ParsedOperand target,
		InstructionStatement instruction,
		uint instructionAddress
	)
	{
		int displacementWidth = instruction.Size == OperandSize.Byte ? 1 : 2;

		if (TryResolveRegisterCode(target, out byte regCode))
		{
			return new BranchTargetResolution(regCode, 0, false);
		}

		long absoluteTarget = ExpressionEvaluator.Evaluate(
			ExtractExpression(target),
			_symbols,
			instructionAddress,
			_diagnostics
		);
		long displacement = absoluteTarget - (instructionAddress + 2 + displacementWidth);
		return new BranchTargetResolution((byte)WideRegister.DirectOrImmediate, displacement, true);
	}

	private BranchTargetResolution ResolveAbsoluteBranchTarget(
		ParsedOperand target,
		int line,
		int column,
		uint instructionAddress
	)
	{
		if (target is IndirectOperand or IndexedOperand)
		{
			MemoryResolution memory = ResolveMemoryOperand(target, line, column, instructionAddress);
			return new BranchTargetResolution(memory.RegisterCode, memory.Immediate, memory.HasImmediate);
		}

		ExpressionNode addressExpr = target is DirectMemoryOperand direct ? direct.Address : ExtractExpression(target);

		long address = ExpressionEvaluator.Evaluate(addressExpr, _symbols, instructionAddress, _diagnostics);
		return new BranchTargetResolution((byte)WideRegister.DirectOrImmediate, address, true);
	}

	private byte ResolveInterruptModeFunctionCode(InstructionStatement instruction, uint instructionAddress)
	{
		if (instruction.Operands.Length != 1)
		{
			throw new AssemblyException(instruction.Line, instruction.Column, "IM requires an operand (1 or 2)");
		}

		long mode = EvaluateOperand(instruction.Operands[0], instructionAddress);

		if (!Keywords.TryGetInterruptModeFunctionCode(mode, out byte functionCode))
		{
			throw new AssemblyException(instruction.Line, instruction.Column, $"IM operand must be 1 or 2, got {mode}");
		}

		return functionCode;
	}

	private MemoryResolution ResolveMemoryOperand(ParsedOperand operand, int line, int column, uint instructionAddress)
	{
		return operand switch
		{
			IndirectOperand indirect => new MemoryResolution((byte)indirect.Register, 0, null),

			IndexedOperand indexed => new MemoryResolution(
				(byte)indexed.Register,
				ExpressionEvaluator.Evaluate(indexed.Displacement, _symbols, instructionAddress, _diagnostics),
				OperandSize.Word
			),

			DirectMemoryOperand direct => new MemoryResolution(
				(byte)WideRegister.DirectOrImmediate,
				ExpressionEvaluator.Evaluate(direct.Address, _symbols, instructionAddress, _diagnostics),
				OperandSize.Dword
			),

			_ => throw new AssemblyException(line, column, $"Expected memory operand, got {operand.GetType().Name}"),
		};
	}

	private void EmitDirective(DirectiveStatement directive)
	{
		switch (directive.Directive)
		{
			case Directive.ORG:
				if (directive.Operands.Length == 1)
				{
					uint newOrigin = (uint)EvaluateOperand(directive.Operands[0]);
					if (newOrigin < _locationCounter)
					{
						throw new AssemblyException(
							directive.Line,
							directive.Column,
							$"ORG 0x{newOrigin:X} is before current address 0x{_locationCounter:X}"
						);
					}
					_locationCounter = newOrigin;
				}
				break;

			case Directive.DB:
				foreach (ParsedOperand operand in directive.Operands)
				{
					if (operand is StringLiteralOperand str)
					{
						foreach (char ch in str.Value)
						{
							EmitByte((byte)ch);
						}
					}
					else
					{
						EmitByte((byte)EvaluateOperand(operand));
					}
				}
				break;

			case Directive.DW:
				foreach (ParsedOperand operand in directive.Operands)
				{
					EmitWord((ushort)EvaluateOperand(operand));
				}
				break;

			case Directive.DD:
				foreach (ParsedOperand operand in directive.Operands)
				{
					EmitDword((uint)EvaluateOperand(operand));
				}
				break;

			case Directive.DS:
			case Directive.DEFS:
			{
				long count = directive.Operands.Length >= 1 ? EvaluateOperand(directive.Operands[0]) : 0;
				byte fillByte = directive.Operands.Length >= 2
					? (byte)(EvaluateOperand(directive.Operands[1]) & 0xFF)
					: (byte)0;

				for (long i = 0; i < count; i++)
				{
					EmitByte(fillByte);
				}
				break;
			}

			case Directive.ALIGN:
				if (directive.Operands.Length >= 1)
				{
					long alignment = EvaluateOperand(directive.Operands[0]);
					if (alignment < 1)
					{
						break; // already reported in pass one
					}

					byte fillByte = directive.Operands.Length >= 2
						? (byte)(EvaluateOperand(directive.Operands[1]) & 0xFF)
						: (byte)0;

					uint padding = AlignmentPadding(_locationCounter, alignment);
					for (uint i = 0; i < padding; i++)
					{
						EmitByte(fillByte);
					}
				}
				break;

			case Directive.EQU:
				break; // handled in pass one

			case Directive.RB:
			case Directive.RS:
				if (directive.Operands.Length >= 1)
				{
					_locationCounter += (uint)EvaluateOperand(directive.Operands[0]);
				}
				break;

			case Directive.RW:
				if (directive.Operands.Length >= 1)
				{
					_locationCounter += (uint)EvaluateOperand(directive.Operands[0]) * 2;
				}
				break;

			case Directive.RD:
				if (directive.Operands.Length >= 1)
				{
					_locationCounter += (uint)EvaluateOperand(directive.Operands[0]) * 4;
				}
				break;

			case Directive.INCBIN:
				if (TryGetIncbinPath(directive, out string path))
				{
					foreach (byte b in File.ReadAllBytes(path))
					{
						EmitByte(b);
					}
				}
				break;
		}
	}

	private static InstructionVariant? MatchVariant(InstructionDefinition definition, InstructionStatement instruction)
	{
		// First pass: prefer size-constrained variants when the instruction has an explicit size suffix.
		if (instruction.Size is not null)
		{
			foreach (InstructionVariant variant in definition.Variants)
			{
				if (variant.RequiredSizes is not null &&
					Array.Exists(variant.RequiredSizes, s => s == instruction.Size) &&
					OperandsMatch(variant, instruction))
				{
					return variant;
				}
			}
		}

		// Second pass: unconstrained variants.
		foreach (InstructionVariant variant in definition.Variants)
		{
			if (variant.RequiredSizes is null && OperandsMatch(variant, instruction))
			{
				return variant;
			}
		}

		return null;
	}

	private static bool OperandsMatch(InstructionVariant variant, InstructionStatement instruction)
	{
		if (instruction.Operands.Length != variant.Operands.Length)
		{
			return false;
		}

		for (int i = 0; i < instruction.Operands.Length; i++)
		{
			if (!variant.Operands[i].Allows(ClassifyAddressingMode(instruction.Operands[i])))
			{
				return false;
			}
		}

		return true;
	}

	private static int InstructionWordSize(InstructionVariant variant)
	{
		return variant.Format == InstructionFormat.FormatS ? 1 : 2;
	}

	private static int AppendedByteCount(InstructionVariant variant, InstructionStatement instruction)
	{
		OperandSize size = ResolveSize(instruction);

		return variant.Format switch
		{
			InstructionFormat.FormatS => instruction.Operands.Length == 1 ? 1 : 0,
			InstructionFormat.FormatBlk => 0,
			InstructionFormat.FormatN => AppendedBytesFormatN(instruction),
			InstructionFormat.FormatB => AppendedBytesFormatB(instruction),
			InstructionFormat.FormatUm => AppendedBytesFormatUm(instruction),
			InstructionFormat.FormatRm => AppendedBytesFormatRm(instruction, size),
			_ => AppendedBytesFormatR(instruction, size),
		};
	}

	private static int AppendedBytesFormatN(InstructionStatement instruction)
	{
		if (instruction.Mnemonic == Mnemonic.RST)
		{
			return 1;
		}

		bool isLoadIRegister = instruction.Mnemonic == Mnemonic.LD &&
			instruction.Operands.Length == 2 &&
			instruction.Operands[0] is SpecialRegisterOperand { Register: SpecialRegister.I } &&
			instruction.Operands[1] is ImmediateOrRegisterOperand;

		return isLoadIRegister ? 4 : 0;
	}

	private static int AppendedBytesFormatB(InstructionStatement instruction)
	{
		int targetIndex = instruction.Operands.Length > 0 && instruction.Operands[0] is ConditionOperand ? 1 : 0;

		if (targetIndex >= instruction.Operands.Length)
		{
			return 0;
		}

		ParsedOperand target = instruction.Operands[targetIndex];
		bool isRelative = instruction.Mnemonic is Mnemonic.JR or Mnemonic.CALLR;

		if (isRelative)
		{
			if (TryResolveRegisterCode(target, out _))
			{
				return 0;
			}

			return instruction.Size == OperandSize.Byte ? 1 : 2;
		}

		return target switch
		{
			IndirectOperand => 0,
			IndexedOperand => 2,
			_ => 4,
		};
	}

	private static int AppendedBytesFormatUm(InstructionStatement instruction)
	{
		if (instruction.Operands.Length == 0)
		{
			return 0;
		}

		return instruction.Operands[0] switch
		{
			IndexedOperand => 2,
			DirectMemoryOperand => 4,
			_ => 0,
		};
	}

	private static int AppendedBytesFormatRm(InstructionStatement instruction, OperandSize size)
	{
		bool isLea = instruction.Mnemonic == Mnemonic.LEA;

		bool firstIsMemory = !isLea && IsMemoryOperand(instruction.Operands[0]);
		ParsedOperand memoryOperand = isLea
			? instruction.Operands[1]
			: firstIsMemory
				? instruction.Operands[0]
				: instruction.Operands[1];
		ParsedOperand? registerOperand = isLea
			? null // dest is a register, never an immediate
			: firstIsMemory
				? instruction.Operands.Length > 1 ? instruction.Operands[1] : null
				: instruction.Operands[0];

		int total = memoryOperand switch
		{
			IndexedOperand => 2,
			DirectMemoryOperand => 4,
			_ => 0,
		};

		if (registerOperand is not null && IsImmediateOperand(registerOperand))
		{
			total += ImmediateByteCount(size);
		}

		return total;
	}

	private static int AppendedBytesFormatR(InstructionStatement instruction, OperandSize size)
	{
		if (instruction.Operands.Length < 2)
		{
			return 0;
		}

		ParsedOperand sourceOperand = instruction.Mnemonic == Mnemonic.LEA
			? instruction.Operands[1]
			: instruction.Operands[^1];

		if (!IsImmediateOperand(sourceOperand))
		{
			return 0;
		}

		// LEA immediates are always Word (the source is an address/value).
		return instruction.Mnemonic == Mnemonic.LEA ? ImmediateByteCount(OperandSize.Word) : ImmediateByteCount(size);
	}

	private static int ImmediateByteCount(OperandSize size)
	{
		return size switch
		{
			OperandSize.Byte => 1,
			OperandSize.Dword => 4,
			_ => 2,
		};
	}

	private static AddressingMode ClassifyAddressingMode(ParsedOperand operand)
	{
		return operand switch
		{
			AltNarrowRegisterOperand => AddressingMode.AltNarrowRegister,
			AltWideRegisterOperand => AddressingMode.AltWideRegister,
			ConditionOperand => AddressingMode.Condition,
			SpecialRegisterOperand => AddressingMode.SpecialRegister,
			IndirectOperand => AddressingMode.Indirect,
			IndexedOperand => AddressingMode.Indexed,
			DirectMemoryOperand => AddressingMode.DirectMemory,
			ImmediateOrRegisterOperand imm => ClassifyExpressionOperand(imm),
			_ => throw new InvalidOperationException($"Unexpected operand type '{operand.GetType().Name}'"),
		};
	}

	private static AddressingMode ClassifyExpressionOperand(ImmediateOrRegisterOperand operand)
	{
		if (operand.Expression is not SymbolReferenceNode sym)
		{
			return AddressingMode.Immediate;
		}

		if (Registers.IsNarrowName(sym.Name))
		{
			return AddressingMode.NarrowRegister;
		}
		if (Registers.IsWideName(sym.Name))
		{
			return AddressingMode.WideRegister;
		}

		return AddressingMode.Immediate;
	}

	private static bool IsMemoryOperand(ParsedOperand operand)
	{
		return operand is IndirectOperand or IndexedOperand or DirectMemoryOperand;
	}

	private static bool IsImmediateOperand(ParsedOperand operand)
	{
		return operand is ImmediateOrRegisterOperand expr &&
			!IsNarrowRegisterExpression(expr) &&
			!IsWideRegisterExpression(expr);
	}

	private static bool IsNarrowRegisterOperand(ParsedOperand operand)
	{
		return operand switch
		{
			AltNarrowRegisterOperand => true,
			ImmediateOrRegisterOperand expr => IsNarrowRegisterExpression(expr),
			_ => false,
		};
	}

	private static bool IsWideRegisterOperand(ParsedOperand operand)
	{
		return operand switch
		{
			AltWideRegisterOperand => true,
			ImmediateOrRegisterOperand expr => IsWideRegisterExpression(expr),
			_ => false,
		};
	}

	private static bool IsNarrowRegisterExpression(ImmediateOrRegisterOperand operand)
	{
		return operand.Expression is SymbolReferenceNode sym && Registers.IsNarrowName(sym.Name);
	}

	private static bool IsWideRegisterExpression(ImmediateOrRegisterOperand operand)
	{
		return operand.Expression is SymbolReferenceNode sym && Registers.IsWideName(sym.Name);
	}

	// -------------------------------------------------------------------------
	// Register code resolution
	// -------------------------------------------------------------------------

	private static byte ResolveRegisterCode(ParsedOperand? operand, int line, int column)
	{
		return operand switch
		{
			ImmediateOrRegisterOperand { Expression: SymbolReferenceNode sym } => ResolveSymbolRegisterCode(
				sym,
				line,
				column
			),
			AltNarrowRegisterOperand alt => (byte)alt.Register,
			AltWideRegisterOperand alt => (byte)alt.Register,
			null => throw new AssemblyException(line, column, "Missing destination operand"),
			_ => throw new AssemblyException(line, column, $"Expected register, got {operand.GetType().Name}"),
		};
	}

	private static byte ResolveSymbolRegisterCode(SymbolReferenceNode sym, int line, int column)
	{
		if (Registers.TryParseNarrow(sym.Name, out NarrowRegister narrow))
		{
			return (byte)narrow;
		}
		if (Registers.TryParseWide(sym.Name, out WideRegister wide))
		{
			return (byte)wide;
		}
		throw new AssemblyException(line, column, "Expected register in destination");
	}

	private byte ResolveRegisterCodeOrImmediate(
		ParsedOperand? operand,
		out long immediateValue,
		int line,
		int column,
		uint instructionAddress
	)
	{
		immediateValue = 0;

		switch (operand)
		{
			case ImmediateOrRegisterOperand { Expression: SymbolReferenceNode sym }:
				if (Registers.TryParseNarrow(sym.Name, out NarrowRegister narrow))
				{
					return (byte)narrow;
				}
				if (Registers.TryParseWide(sym.Name, out WideRegister wide))
				{
					return (byte)wide;
				}

				immediateValue = ExpressionEvaluator.Evaluate(sym, _symbols, instructionAddress, _diagnostics);
				return (byte)NarrowRegister.Immediate;

			case ImmediateOrRegisterOperand expr:
				immediateValue = ExpressionEvaluator.Evaluate(
					expr.Expression,
					_symbols,
					instructionAddress,
					_diagnostics
				);
				return (byte)NarrowRegister.Immediate;

			case null:
				throw new AssemblyException(line, column, "Missing source operand");

			default:
				throw new AssemblyException(
					line,
					column,
					$"Expected register or immediate, got {operand.GetType().Name}"
				);
		}
	}

	private static bool TryResolveRegisterCode(ParsedOperand operand, out byte registerCode)
	{
		if (operand is ImmediateOrRegisterOperand { Expression: SymbolReferenceNode sym })
		{
			if (Registers.TryParseNarrow(sym.Name, out NarrowRegister narrow))
			{
				registerCode = (byte)narrow;
				return true;
			}
			if (Registers.TryParseWide(sym.Name, out WideRegister wide))
			{
				registerCode = (byte)wide;
				return true;
			}
		}

		registerCode = 0;
		return false;
	}

	private static ExpressionNode ExtractExpression(ParsedOperand operand)
	{
		return operand is ImmediateOrRegisterOperand expr
			? expr.Expression
			: throw new InvalidOperationException($"Cannot extract expression from {operand.GetType().Name}");
	}

	private long EvaluateOperand(ParsedOperand operand, uint currentAddress)
	{
		return ExpressionEvaluator.Evaluate(ExtractExpression(operand), _symbols, currentAddress, _diagnostics);
	}

	private long EvaluateOperand(ParsedOperand operand)
	{
		return EvaluateOperand(operand, _locationCounter);
	}

	private void EmitByte(byte value)
	{
		_output.Add(value);
		_locationCounter += 1;
	}

	private void EmitWord(ushort value)
	{
		_output.Add((byte)(value & 0xFF));
		_output.Add((byte)(value >> 8));
		_locationCounter += 2;
	}

	private void EmitDword(uint value)
	{
		_output.Add((byte)(value & 0xFF));
		_output.Add((byte)((value >> 8) & 0xFF));
		_output.Add((byte)((value >> 16) & 0xFF));
		_output.Add((byte)(value >> 24));
		_locationCounter += 4;
	}

	private void EmitImmediate(long value, OperandSize size, int line, int column)
	{
		switch (size)
		{
			case OperandSize.Byte:
				if (value is < -128 or > 255)
				{
					Warn(line, column, $"Immediate {value} truncated to byte");
				}
				EmitByte((byte)(value & 0xFF));
				break;

			case OperandSize.Word:
				if (value is < -32768 or > 65535)
				{
					Warn(line, column, $"Immediate {value} truncated to word");
				}
				EmitWord((ushort)(value & 0xFFFF));
				break;

			case OperandSize.Dword:
				if (value is < -2147483648L or > 4294967295L)
				{
					Warn(line, column, $"Immediate {value} truncated to dword");
				}
				EmitDword((uint)(value & 0xFFFFFFFF));
				break;
		}
	}

	private static uint AlignmentPadding(uint address, long alignment)
	{
		return (uint)((alignment - (address % alignment)) % alignment);
	}

	private bool TryGetIncbinPath(DirectiveStatement directive, out string path)
	{
		path = string.Empty;

		if (directive.Operands.Length != 1 || directive.Operands[0] is not StringLiteralOperand pathOp)
		{
			return false;
		}

		if (!File.Exists(pathOp.Value))
		{
			_diagnostics.Add(
				new Diagnostic(
					directive.Line,
					directive.Column,
					DiagnosticSeverity.Error,
					$"INCBIN: file not found: '{pathOp.Value}'"
				)
			);
			return false;
		}

		path = pathOp.Value;
		return true;
	}

	private static HashSet<string> BuildReservedNames()
	{
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		names.UnionWith(Enum.GetValues<Directive>().Select(d => d.ToString()));
		names.UnionWith(IsaTable.Instructions.Keys);
		names.UnionWith(Registers.ByNarrowName.Keys);
		names.UnionWith(Registers.ByWideName.Keys);
		return names;
	}

	private void Warn(int line, int column, string message)
	{
		_diagnostics.Add(new Diagnostic(line, column, DiagnosticSeverity.Warning, message));
	}
}
