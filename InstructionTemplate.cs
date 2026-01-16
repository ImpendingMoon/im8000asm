using System.Diagnostics;

namespace im8000asm;

internal interface IInstructionTemplate
{
    byte[] Encode();
}

internal class RTypeInstructionTemplate : IInstructionTemplate
{
    public byte Group => 0b00;
    public byte Opcode { get; set; }
    public Constants.RegisterTarget Operand1 { get; set; }
    public Constants.RegisterTarget? Operand2 { get; set; }
    public uint? Immediate { get; set; }
    public Constants.OperandSize OperandSize { get; set; }

    public byte[] Encode()
    {
        if (Operand2 is null && Immediate is null)
        {
            throw new ArgumentException("Internal error: At least one Operand 2 must be set");
        }

        byte low = (byte)(Group | (Opcode << 2));

        if (Operand2 is not null)
        {
            byte operandSize = EncodeOperandSize(OperandSize);
            byte operand1 = EncodeRegisterTarget(Operand1, OperandSize);
            byte operand2 = EncodeRegisterTarget(Operand2.Value, OperandSize);

            byte high = (byte)(operandSize | (operand1 << 2) | (operand2 << 5));

            return [low, high];
        }
        else
        {
            Debug.Assert(Immediate is not null);

            byte operandSize = EncodeOperandSize(OperandSize);
            byte operand1 = EncodeRegisterTarget(Operand1, OperandSize);

            byte high = (byte)(operandSize | (operand1 << 2) | (0b111 << 5));

            byte[] immediate = OperandSize switch
            {
                Constants.OperandSize.Byte => [(byte)Immediate.Value],
                Constants.OperandSize.Word => BitConverter.GetBytes((ushort)Immediate.Value),
                Constants.OperandSize.DWord => BitConverter.GetBytes(Immediate.Value),
                _ => throw new UnreachableException("Illegal state")
            };

            byte[] result = new byte[2 + immediate.Length];
            result[0] = low;
            result[1] = high;
            immediate.CopyTo(result.AsSpan(2));

            return result;
        }
    }

    internal class RMTypeInstructionTemplate : IInstructionTemplate
    {
        public byte Group => 0b01;
        public byte Opcode { get; set; }
        public Constants.RegisterTarget RegisterTarget { get; set; }
        public Constants.RegisterTarget? AddressRegisterTarget { get; set; }
        public uint? Direct { get; set; }
        public short? Displacement { get; set; }
        public Constants.OperandSize OperandSize { get; set; }

        public byte[] Encode()
        {
            return [];
        }
    }

    internal class URTypeInstructionTemplate : IInstructionTemplate
    {
        public byte Group => 0b10;
        public byte Subgroup => 0b00;
        public byte Opcode { get; set; }
        public byte Function { get; set; }
        public Constants.RegisterTarget RegisterTarget { get; set; }
        public Constants.OperandSize OperandSize { get; set; }

        public byte[] Encode()
        {
            return [];
        }

        internal class UMTypeInstructionTemplate : IInstructionTemplate
        {
            public byte Group => 0b10;
            public byte Subgroup => 0b01;
            public byte Opcode { get; set; }
            public byte Function { get; set; }
            public Constants.RegisterTarget? AddressRegisterTarget { get; set; }
            public uint? Direct { get; set; }
            public Constants.OperandSize OperandSize { get; set; }

            public byte[] Encode()
            {
                return [];
            }
        }

        internal class BTypeInstructionTemplate : IInstructionTemplate
        {
            public byte Group => 0b10;
            public byte Subgroup => 0b10;
            public byte Opcode { get; set; }
            public byte Condition { get; set; }
            public Constants.RegisterTarget? AddressRegisterTarget { get; set; }
            public uint? Direct { get; set; }
            public Constants.OperandSize OperandSize { get; set; }

            public byte[] Encode()
            {
                return [];
            }
        }

        internal class NTypeInstructionTemplate : IInstructionTemplate
        {
            public byte Group => 0b10;
            public byte Subgroup => 0b10;
            public byte Opcode { get; set; }
            public byte Function { get; set; }
            public uint? Immediate { get; set; }

            public byte[] Encode()
            {
                return [];
            }
        }

        internal class SBTypeInstructionTemplate : IInstructionTemplate
        {
            public byte Group => 0b10;
            public byte Subgroup => 0b10;
            public byte Opcode { get; set; }
            public byte? Immediate { get; set; }

            public byte[] Encode()
            {
                return [];
            }
        }

        private byte EncodeOperandSize(Constants.OperandSize size)
        {
            return size switch
            {
                Constants.OperandSize.Byte => 0b00,
                Constants.OperandSize.Word => 0b01,
                Constants.OperandSize.DWord => 0b10,
                _ => throw new NotImplementedException($"{size} cannot be directly encoded")
            };
        }

        private byte EncodeRegisterTarget(Constants.RegisterTarget target, Constants.OperandSize size)
        {
            if (size == Constants.OperandSize.Byte || size == Constants.OperandSize.Word)
            {
                return target switch
                {
                    Constants.RegisterTarget.A => 0b000,
                    Constants.RegisterTarget.B => 0b001,
                    Constants.RegisterTarget.C => 0b010,
                    Constants.RegisterTarget.D => 0b011,
                    Constants.RegisterTarget.E => 0b100,
                    Constants.RegisterTarget.H => 0b101,
                    Constants.RegisterTarget.L => 0b110,
                    _ => throw new NotImplementedException($"Register Target {target} is not usable with operand size {size}"),
                };
            }
            else
            {
                return target switch
                {
                    Constants.RegisterTarget.AF => 0b000,
                    Constants.RegisterTarget.BC => 0b001,
                    Constants.RegisterTarget.DE => 0b010,
                    Constants.RegisterTarget.HL => 0b011,
                    Constants.RegisterTarget.IX => 0b100,
                    Constants.RegisterTarget.IY => 0b101,
                    Constants.RegisterTarget.SP => 0b110,
                    _ => throw new NotImplementedException($"Register Target {target} is not usable with operand size {size}"),
                };
            }
        }
    }