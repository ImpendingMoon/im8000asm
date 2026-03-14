namespace im8000asm;

public record ListingRecord(uint Address, int ByteOffset, int ByteCount, int SourceLine);

public record AssembledOutput(
	byte[] Bytes,
	IReadOnlyDictionary<string, uint> SymbolTable,
	IReadOnlyList<Diagnostic> Diagnostics,
	IReadOnlyList<ListingRecord> Listing
);

public record MemoryResolution(byte RegisterCode, long Displacement, bool HasDisplacement);

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
	private const ushort SBBaseBits = 0b1111;
	private static readonly HashSet<string> ReservedNames = BuildReservedNames();

	private readonly List<Diagnostic> diagnostics = [];
	private readonly List<ListingRecord> listing = [];
	private readonly List<byte> output = [];
	private readonly List<ParsedStatement> statements;
	private readonly Dictionary<string, uint> symbols = new();

	private uint initialOrigin;
	private uint origin;
	private uint programCounter;

	public CodeGenerator(List<ParsedStatement> statements)
	{
		this.statements = statements;
	}

	public AssembledOutput Assemble()
	{
		PassOne();

		try
		{
			PassTwo();
		}
		catch (AssemblyException exception)
		{
			diagnostics.Add(
				new Diagnostic(exception.Line, exception.Column, DiagnosticSeverity.Error, exception.Message)
			);
		}

		return new AssembledOutput(output.ToArray(), symbols, diagnostics, listing);
	}

	private void PassOne()
	{
		initialOrigin = origin;
		programCounter = origin;

		foreach (ParsedStatement statement in statements)
		{
			switch (statement)
			{
				case LabelStatement label:
					RegisterLabel(label);
					break;

				case DirectiveStatement directive:
					HandleDirectivePassOne(directive);
					break;

				case InstructionStatement instruction:
					programCounter += (uint)MeasureInstruction(instruction);
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

		if (!symbols.TryAdd(label.Name, programCounter))
		{
			throw new AssemblyException(label.Line, label.Column, $"Duplicate label '{label.Name}'");
		}
	}

	private void HandleDirectivePassOne(DirectiveStatement directive)
	{
		switch (directive.Directive)
		{
			case Directive.ORG:
				if (directive.Operands.Length == 1)
				{
					origin = programCounter = (uint)EvaluateOperand(directive.Operands[0]);
				}
				break;

			case Directive.DB:
				foreach (ParsedOperand operand in directive.Operands)
				{
					programCounter += operand is StringLiteralOperand s ? (uint)s.Value.Length : 1u;
				}
				break;

			case Directive.DW:
				programCounter += (uint)directive.Operands.Length * 2;
				break;

			case Directive.DD:
				programCounter += (uint)directive.Operands.Length * 4;
				break;

			case Directive.DS:
			case Directive.DEFS:
				if (directive.Operands.Length >= 1)
				{
					programCounter += (uint)EvaluateOperand(directive.Operands[0]);
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
					uint padding = (uint)((alignment - (programCounter % alignment)) % alignment);
					programCounter += padding;
				}
				break;

			case Directive.EQU:
				HandleEquDirective(directive);
				break;

			case Directive.INCBIN:
				if (directive.Operands.Length == 1 && directive.Operands[0] is StringLiteralOperand pathOperand)
				{
					if (!File.Exists(pathOperand.Value))
					{
						throw new AssemblyException(
							directive.Line,
							directive.Column,
							$"INCBIN: file not found: '{pathOperand.Value}'"
						);
					}

					programCounter += (uint)new FileInfo(pathOperand.Value).Length;
				}
				break;
		}
	}

	private void HandleEquDirective(DirectiveStatement directive)
	{
		if (!directive.HasLabel)
		{
			Warn(directive.Line, directive.Column, ".EQU has no label — directive ignored");
			return;
		}

		if (ReservedNames.Contains(directive.LabelName))
		{
			Warn(
				directive.Line,
				directive.Column,
				$"'{directive.LabelName}' is a reserved name and cannot be used as an EQU symbol"
			);
			return;
		}

		if (directive.Operands.Length == 0)
		{
			Warn(
				directive.Line,
				directive.Column,
				$".EQU for '{directive.LabelName}' has no value — directive ignored"
			);
			return;
		}

		symbols[directive.LabelName] = (uint)EvaluateOperand(directive.Operands[0]);
	}

	private void PassTwo()
	{
		programCounter = initialOrigin;

		foreach (ParsedStatement statement in statements)
		{
			uint addressBefore = programCounter;
			int offsetBefore = output.Count;

			switch (statement)
			{
				case DirectiveStatement directive:
					EmitDirective(directive);
					break;

				case InstructionStatement instruction:
					EmitInstruction(instruction);
					break;
			}

			listing.Add(new ListingRecord(addressBefore, offsetBefore, output.Count - offsetBefore, statement.Line));
		}
	}

	private static InstructionDefinition LookupDefinition(InstructionStatement instruction)
	{
		if (!IsaTable.Instructions.TryGetValue(instruction.Mnemonic.ToString(), out InstructionDefinition? definition))
		{
			throw new AssemblyException(
				instruction.Line,
				instruction.Column,
				$"Unknown mnemonic '{instruction.Mnemonic}'"
			);
		}

		return definition;
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
			if (hasWideRegisterOperand && instruction.Size == OperandSize.Dword)
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

	private void EmitFormatR(InstructionVariant variant, InstructionStatement instruction)
	{
		OperandSize size = ResolveSize(instruction);
		uint instructionAddress = programCounter;

		ParsedOperand? destination = instruction.Operands.Length > 0 ? instruction.Operands[0] : null;
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
		word |= (ushort)((int)size << 8);
		word |= (ushort)(destinationCode << 10);
		word |= (ushort)(sourceCode << 13);

		EmitWord(word);
		programCounter += 2;

		if (sourceCode == (byte)NarrowRegister.Immediate)
		{
			EmitImmediate(immediateValue, size, instruction.Line, instruction.Column);
		}
	}

	private void EmitFormatRm(InstructionVariant variant, InstructionStatement instruction)
	{
		OperandSize size = ResolveSize(instruction);
		uint instructionAddress = programCounter;

		bool destinationIsMemory = IsMemoryOperand(instruction.Operands[0]);
		byte direction = destinationIsMemory ? (byte)0 : (byte)1;

		ParsedOperand memoryOperand = destinationIsMemory ? instruction.Operands[0] : instruction.Operands[1];
		ParsedOperand registerOperand = destinationIsMemory ? instruction.Operands[1] : instruction.Operands[0];

		long immediateValue = 0;
		bool registerSideIsImmediate = IsImmediateOperand(registerOperand);
		bool memorySideIsImmediate = direction == 0 && IsImmediateOperand(memoryOperand);

		byte registerCode;
		if (memorySideIsImmediate)
		{
			registerCode = ResolveRegisterCode(registerOperand, instruction.Line, instruction.Column);
		}
		else if (registerSideIsImmediate)
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

		ushort word = RMBaseBits;
		word |= (ushort)(variant.Opcode << 2);
		word |= (ushort)(direction << 7);
		word |= (ushort)((int)size << 8);
		word |= (ushort)(registerCode << 10);
		word |= (ushort)(memory.RegisterCode << 13);

		EmitWord(word);
		programCounter += 2;

		if (memory.HasDisplacement)
		{
			EmitImmediate(memory.Displacement, OperandSize.Dword, instruction.Line, instruction.Column);
		}

		if (registerCode == (byte)NarrowRegister.Immediate)
		{
			EmitImmediate(immediateValue, size, instruction.Line, instruction.Column);
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
					"EX r, r' requires an alternate-register operand (e.g. A' or HL')"
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
		programCounter += 2;
	}

	private void EmitFormatUm(InstructionVariant variant, InstructionStatement instruction)
	{
		OperandSize size = ResolveSize(instruction);
		uint instructionAddress = programCounter;

		MemoryResolution memory = ResolveMemoryOperand(
			instruction.Operands[0],
			instruction.Line,
			instruction.Column,
			instructionAddress
		);

		ushort word = UMBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)((int)size << 8);
		word |= (ushort)(variant.FunctionCode << 10);
		word |= (ushort)(memory.RegisterCode << 13);

		EmitWord(word);
		programCounter += 2;

		if (memory.HasDisplacement)
		{
			EmitImmediate(memory.Displacement, OperandSize.Dword, instruction.Line, instruction.Column);
		}
	}

	private void EmitFormatB(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = programCounter;

		byte conditionCode = (byte)BranchCondition.Always;
		int targetIndex = 0;

		if (instruction.Operands.Length > 0 && instruction.Operands[0] is ConditionOperand conditionOperand)
		{
			conditionCode = (byte)conditionOperand.Condition;
			targetIndex = 1;
		}

		byte addressCode = 0b000;
		long appendedValue = 0;
		bool hasAppended = false;
		bool isRelative = instruction.Mnemonic is Mnemonic.JR or Mnemonic.CALLR;

		if (targetIndex < instruction.Operands.Length)
		{
			ParsedOperand target = instruction.Operands[targetIndex];

			BranchTargetResolution resolution = isRelative
				? ResolveRelativeBranchTarget(target, instruction, instructionAddress)
				: ResolveAbsoluteBranchTarget(target, instruction.Line, instruction.Column, instructionAddress);

			addressCode = resolution.AddressCode;
			appendedValue = resolution.AppendedValue;
			hasAppended = resolution.HasAppended;
		}

		ushort word = BBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)(conditionCode << 9);
		word |= (ushort)(addressCode << 13);

		EmitWord(word);
		programCounter += 2;

		if (hasAppended)
		{
			OperandSize appendSize;
			if (isRelative)
			{
				appendSize = instruction.Size == OperandSize.Byte ? OperandSize.Byte : OperandSize.Word;
			}
			else
			{
				appendSize = OperandSize.Dword;
			}

			EmitImmediate(appendedValue, appendSize, instruction.Line, instruction.Column);
		}
	}

	private BranchTargetResolution ResolveRelativeBranchTarget(
		ParsedOperand target,
		InstructionStatement instruction,
		uint instructionAddress
	)
	{
		int displacementWidth = instruction.Size == OperandSize.Byte ? 1 : 2;

		if (TryResolveRegisterCode(target, out byte registerCode))
		{
			return new BranchTargetResolution(registerCode, 0, false);
		}

		long absoluteTarget = ExpressionEvaluator.Evaluate(
			ExtractExpression(target),
			symbols,
			instructionAddress,
			diagnostics
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
			return new BranchTargetResolution(memory.RegisterCode, memory.Displacement, memory.HasDisplacement);
		}

		long address = target is DirectMemoryOperand
			? ResolveMemoryOperand(target, line, column, instructionAddress).Displacement
			: ExpressionEvaluator.Evaluate(ExtractExpression(target), symbols, instructionAddress, diagnostics);

		return new BranchTargetResolution((byte)WideRegister.DirectOrImmediate, address, true);
	}

	private void EmitFormatN(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = programCounter;
		byte functionCode = instruction.Mnemonic == Mnemonic.IM
			? ResolveInterruptModeFunctionCode(instruction, instructionAddress)
			: variant.FunctionCode;

		ushort word = NBaseBits;
		word |= (ushort)(variant.Opcode << 4);
		word |= (ushort)(functionCode << 8);

		EmitWord(word);
		programCounter += 2;

		bool isLoadSpecialRegister = instruction.Mnemonic == Mnemonic.LD &&
			instruction.Operands.Length >= 1 &&
			instruction.Operands[0] is SpecialRegisterOperand { Register: SpecialRegister.I };

		if (instruction.Mnemonic == Mnemonic.RST)
		{
			EmitImmediate(
				EvaluateOperand(instruction.Operands[0], programCounter),
				OperandSize.Byte,
				instruction.Line,
				instruction.Column
			);
		}
		else if (isLoadSpecialRegister)
		{
			EmitImmediate(
				EvaluateOperand(instruction.Operands[1], programCounter),
				OperandSize.Dword,
				instruction.Line,
				instruction.Column
			);
		}
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

	private void EmitFormatS(InstructionVariant variant, InstructionStatement instruction)
	{
		uint instructionAddress = programCounter;

		byte word = (byte)SBBaseBits;
		word |= (byte)(variant.Opcode << 4);
		EmitByte(word);
		programCounter += 1;

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
		programCounter += 2;
	}

	private MemoryResolution ResolveMemoryOperand(ParsedOperand operand, int line, int column, uint instructionAddress)
	{
		switch (operand)
		{
			case IndirectOperand indirect:
				return new MemoryResolution((byte)indirect.Register, 0, false);

			case IndexedOperand indexed:
				long indexedDisplacement = ExpressionEvaluator.Evaluate(
					indexed.Displacement,
					symbols,
					instructionAddress,
					diagnostics
				);
				return new MemoryResolution((byte)indexed.Register, indexedDisplacement, true);

			case DirectMemoryOperand directMemory:
				long directAddress = ExpressionEvaluator.Evaluate(
					directMemory.Address,
					symbols,
					instructionAddress,
					diagnostics
				);
				return new MemoryResolution((byte)WideRegister.DirectOrImmediate, directAddress, true);

			default:
				throw new AssemblyException(line, column, $"Expected memory operand, got {operand.GetType().Name}");
		}
	}

	private static InstructionVariant? MatchVariant(InstructionDefinition definition, InstructionStatement instruction)
	{
		// Prefer size-constrained variants when the instruction has an explicit size suffix,
		// then fall back to unconstrained variants.
		foreach (InstructionVariant variant in definition.Variants.OrderByDescending(v => v.RequiredSizes is not null))
		{
			bool sizeOk = variant.RequiredSizes is null ||
				(instruction.Size is not null && Array.Exists(variant.RequiredSizes, s => s == instruction.Size));

			if (sizeOk && OperandsMatch(variant, instruction))
			{
				return variant;
			}
		}

		return null;
	}

	private static bool OperandsMatch(InstructionVariant variant, InstructionStatement instruction)
	{
		return instruction.Operands.Length == variant.Operands.Length &&
			instruction.Operands.Select((op, i) => variant.Operands[i].Allows(ClassifyAddressingMode(op))).All(x => x);
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

		bool isLoadSpecialRegister = instruction.Mnemonic == Mnemonic.LD &&
			instruction.Operands.Length >= 1 &&
			instruction.Operands[0] is SpecialRegisterOperand { Register: SpecialRegister.I };

		return isLoadSpecialRegister ? 4 : 0;
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

		return target is IndirectOperand ? 0 : 4;
	}

	private static int AppendedBytesFormatUm(InstructionStatement instruction)
	{
		return instruction.Operands.Length > 0 && instruction.Operands[0] is IndexedOperand or DirectMemoryOperand
			? 4
			: 0;
	}

	private static int AppendedBytesFormatRm(InstructionStatement instruction, OperandSize size)
	{
		int total = 0;

		ParsedOperand? memoryOperand;
		if (IsMemoryOperand(instruction.Operands[0]))
		{
			memoryOperand = instruction.Operands[0];
		}
		else if (instruction.Operands.Length > 1 && IsMemoryOperand(instruction.Operands[1]))
		{
			memoryOperand = instruction.Operands[1];
		}
		else
		{
			memoryOperand = null;
		}

		if (memoryOperand is IndexedOperand or DirectMemoryOperand)
		{
			total += 4;
		}

		ParsedOperand? registerSideOperand;
		if (IsMemoryOperand(instruction.Operands[0]))
		{
			registerSideOperand = instruction.Operands.Length > 1 ? instruction.Operands[1] : null;
		}
		else
		{
			registerSideOperand = instruction.Operands[0];
		}

		if (registerSideOperand is not null && IsImmediateOperand(registerSideOperand))
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

		ParsedOperand lastOperand = instruction.Operands[^1];
		bool isImmediate = lastOperand is ImmediateOrRegisterOperand expression &&
			!IsNarrowRegisterExpression(expression);

		return isImmediate ? ImmediateByteCount(size) : 0;
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

	private void EmitDirective(DirectiveStatement directive)
	{
		switch (directive.Directive)
		{
			case Directive.ORG:
				if (directive.Operands.Length == 1)
				{
					uint newOrigin = (uint)EvaluateOperand(directive.Operands[0]);
					while (programCounter < newOrigin)
					{
						EmitByte(0x00);
						programCounter++;
					}
					origin = programCounter = newOrigin;
				}
				break;

			case Directive.DB:
				foreach (ParsedOperand operand in directive.Operands)
				{
					if (operand is StringLiteralOperand stringLiteral)
					{
						foreach (char character in stringLiteral.Value)
						{
							EmitByte((byte)(character & 0x7F));
							programCounter++;
						}
					}
					else
					{
						EmitByte((byte)(EvaluateOperand(operand) & 0xFF));
						programCounter++;
					}
				}
				break;

			case Directive.DW:
				foreach (ParsedOperand operand in directive.Operands)
				{
					EmitWord((ushort)(EvaluateOperand(operand) & 0xFFFF));
					programCounter += 2;
				}
				break;

			case Directive.DD:
				foreach (ParsedOperand operand in directive.Operands)
				{
					EmitDword((uint)(EvaluateOperand(operand) & 0xFFFFFFFF));
					programCounter += 4;
				}
				break;

			case Directive.DS:
			case Directive.DEFS:
				long count = directive.Operands.Length >= 1 ? EvaluateOperand(directive.Operands[0]) : 0;
				byte fillByte = directive.Operands.Length >= 2
					? (byte)(EvaluateOperand(directive.Operands[1]) & 0xFF)
					: (byte)0;

				for (long index = 0; index < count; index++)
				{
					EmitByte(fillByte);
					programCounter++;
				}
				break;

			case Directive.ALIGN:
				if (directive.Operands.Length >= 1)
				{
					long alignment = EvaluateOperand(directive.Operands[0]);
					if (alignment < 1)
					{
						break; // already reported in pass one
					}
					byte alignFill = directive.Operands.Length >= 2
						? (byte)(EvaluateOperand(directive.Operands[1]) & 0xFF)
						: (byte)0;
					uint padding = (uint)((alignment - (programCounter % alignment)) % alignment);
					for (uint i = 0; i < padding; i++)
					{
						EmitByte(alignFill);
						programCounter++;
					}
				}
				break;

			case Directive.EQU:
				break; // handled in pass one

			case Directive.INCBIN:
				if (directive.Operands.Length == 1 && directive.Operands[0] is StringLiteralOperand pathOperand)
				{
					foreach (byte byteValue in File.ReadAllBytes(pathOperand.Value))
					{
						EmitByte(byteValue);
						programCounter++;
					}
				}
				break;
		}
	}

	private static bool IsMemoryOperand(ParsedOperand operand)
	{
		return operand is IndirectOperand or IndexedOperand or DirectMemoryOperand;
	}

	private static bool IsImmediateOperand(ParsedOperand operand)
	{
		return operand is ImmediateOrRegisterOperand expression &&
			!IsNarrowRegisterExpression(expression) &&
			!IsWideRegisterExpression(expression);
	}

	private static bool IsNarrowRegisterOperand(ParsedOperand operand)
	{
		return operand switch
		{
			AltNarrowRegisterOperand => true,
			ImmediateOrRegisterOperand expression => IsNarrowRegisterExpression(expression),
			_ => false,
		};
	}

	private static bool IsWideRegisterOperand(ParsedOperand operand)
	{
		return operand switch
		{
			AltWideRegisterOperand => true,
			ImmediateOrRegisterOperand expression => IsWideRegisterExpression(expression),
			_ => false,
		};
	}

	private static bool IsNarrowRegisterExpression(ImmediateOrRegisterOperand operand)
	{
		return operand.Expression is SymbolReferenceNode symbol && Registers.IsNarrowName(symbol.Name);
	}

	private static bool IsWideRegisterExpression(ImmediateOrRegisterOperand operand)
	{
		return operand.Expression is SymbolReferenceNode symbol && Registers.IsWideName(symbol.Name);
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
			ImmediateOrRegisterOperand expression => ClassifyExpressionOperand(expression),
			_ => AddressingMode.Immediate,
		};
	}

	private static AddressingMode ClassifyExpressionOperand(ImmediateOrRegisterOperand operand)
	{
		if (operand.Expression is not SymbolReferenceNode symbol)
		{
			return AddressingMode.Immediate;
		}

		if (Registers.IsNarrowName(symbol.Name))
		{
			return AddressingMode.NarrowRegister;
		}
		if (Registers.IsWideName(symbol.Name))
		{
			return AddressingMode.WideRegister;
		}

		return AddressingMode.Immediate;
	}

	private static byte ResolveRegisterCode(ParsedOperand? operand, int line, int column)
	{
		if (operand is ImmediateOrRegisterOperand { Expression: SymbolReferenceNode symbol })
		{
			if (Registers.TryParseNarrow(symbol.Name, out NarrowRegister narrow))
			{
				return (byte)narrow;
			}
			if (Registers.TryParseWide(symbol.Name, out WideRegister wide))
			{
				return (byte)wide;
			}
			throw new AssemblyException(line, column, "Expected register in destination");
		}

		return operand switch
		{
			AltNarrowRegisterOperand alt => (byte)alt.Register,
			AltWideRegisterOperand alt => (byte)alt.Register,
			null => throw new AssemblyException(line, column, "Missing destination operand"),
			_ => throw new AssemblyException(line, column, $"Expected register, got {operand.GetType().Name}"),
		};
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
			case ImmediateOrRegisterOperand { Expression: SymbolReferenceNode symbol }:
				if (Registers.TryParseNarrow(symbol.Name, out NarrowRegister narrow))
				{
					return (byte)narrow;
				}
				if (Registers.TryParseWide(symbol.Name, out WideRegister wide))
				{
					return (byte)wide;
				}

				immediateValue = ExpressionEvaluator.Evaluate(
					new SymbolReferenceNode(symbol.Name, symbol.Line, symbol.Column),
					symbols,
					instructionAddress,
					diagnostics
				);
				return (byte)NarrowRegister.Immediate;

			case ImmediateOrRegisterOperand expression:
				immediateValue = ExpressionEvaluator.Evaluate(
					expression.Expression,
					symbols,
					instructionAddress,
					diagnostics
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
		if (operand is ImmediateOrRegisterOperand { Expression: SymbolReferenceNode symbol })
		{
			if (Registers.TryParseNarrow(symbol.Name, out NarrowRegister narrow))
			{
				registerCode = (byte)narrow;
				return true;
			}
			if (Registers.TryParseWide(symbol.Name, out WideRegister wide))
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
		return operand is ImmediateOrRegisterOperand expression
			? expression.Expression
			: throw new InvalidOperationException($"Cannot extract expression from {operand.GetType().Name}");
	}

	private long EvaluateOperand(ParsedOperand operand, uint currentAddress)
	{
		return ExpressionEvaluator.Evaluate(ExtractExpression(operand), symbols, currentAddress, diagnostics);
	}

	private long EvaluateOperand(ParsedOperand operand)
	{
		return EvaluateOperand(operand, programCounter);
	}

	private void EmitByte(byte value)
	{
		output.Add(value);
	}

	private void EmitWord(ushort value)
	{
		output.Add((byte)(value & 0xFF));
		output.Add((byte)(value >> 8));
	}

	private void EmitDword(uint value)
	{
		output.Add((byte)(value & 0xFF));
		output.Add((byte)((value >> 8) & 0xFF));
		output.Add((byte)((value >> 16) & 0xFF));
		output.Add((byte)(value >> 24));
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
				programCounter += 1;
				break;

			case OperandSize.Word:
				if (value is < -32768 or > 65535)
				{
					Warn(line, column, $"Immediate {value} truncated to word");
				}
				EmitWord((ushort)(value & 0xFFFF));
				programCounter += 2;
				break;

			case OperandSize.Dword:
				if (value is < -2147483648L or > 4294967295L)
				{
					Warn(line, column, $"Immediate {value} truncated to dword");
				}
				EmitDword((uint)(value & 0xFFFFFFFF));
				programCounter += 4;
				break;
		}
	}

	private static HashSet<string> BuildReservedNames()
	{
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		names.UnionWith(Enum.GetValues<Directive>().Select(d => d.ToString()));
		names.UnionWith(IsaTable.Instructions.Keys);
		names.UnionWith(Registers.ByNarrowName.Keys);
		names.UnionWith(Registers.ByWideName.Keys);
		names.UnionWith(Keywords.SizeSuffixNames);
		return names;
	}

	private void Warn(int line, int column, string message)
	{
		diagnostics.Add(new Diagnostic(line, column, DiagnosticSeverity.Warning, message));
	}
}
