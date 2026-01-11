namespace im8000asm;

internal static class Constants
{
    public enum RegisterTarget
    {
        A, B, C, D, E, H, L,
        AF, BC, DE, HL, IX, IY, SP,
        I, R,
    }

    public static string[] RegisterTargetNames { get; } = Enum.GetNames<RegisterTarget>();

    public enum OperandSize
    {
        Implied,
        Byte,
        Word,
        DWord,
    }

    public enum Mnemonic
    {
        // Load and store
        LD,     // Load
        EX,     // Exchange
        EXX,    // Exchange Primary
        EXI,    // Exchange Index
        EXH,    // Exchange Halves
        PUSH,   // Push to stack
        POP,    // Pop from stack
        IN,     // In from IO
        OUT,    // Out to IO

        // Block operations
        LDI,    // Load and Increment
        LDIR,   // Load, Increment, and Repeat
        LDD,    // Load and Decrement
        LDDR,   // Load, Decrement, and Repeat
        CPI,    // Compare and Increment
        CPIR,   // Compare, Increment, and Repeat
        CPD,    // Compare and Decrement
        CPDR,   // Compare, Decrement, and Repeat
        TSI,    // Test and Increment
        TSIR,   // Test, Increment, and Repeat
        TSD,    // Test and Decrement
        TSDR,   // Test, Decrement, and Repeat
        INI,    // In and Increment
        INIR,   // In, Increment, and Repeat
        IND,    // In and Decrement
        INDR,   // In, Decrement, and Repeat
        OUTI,   // Out and Increment
        OTIR,   // Out, Increment, and Repeat
        OUTD,   // Out and Decrement
        OTDR,   // Out, Decrement, and Repeat

        // Arithmetic
        ADD,    // Add
        ADC,    // Add with Carry
        SUB,    // Subtract
        SBC,    // Subtract with Carry
        CP,     // Compare
        INC,    // Increment
        DEC,    // Decrement
        DAA,    // Decimal Adjust A
        NEG,    // Negate
        EXT,    // Sign Extend
        MLT,    // Multiply
        DIV,    // Divide
        SDIV,   // Signed Divide

        // Logical
        AND,    // Bitwise AND
        OR,     // Bitwise OR
        XOR,    // Bitwise XOR
        TST,    // Test
        CPL,    // Complement
        BIT,    // Test Bit
        SET,    // Set Bit
        RES,    // Reset Bit
        RLC,    // Rotate Left with Carry
        RRC,    // Rotate Right with Carry
        RL,     // Rotate Left
        RR,     // Rotate Right
        SLA,    // Shift Left Arithmetic
        SRA,    // Shift Right Arithmetic
        SRL,    // Shift Right Logical
        RLD,    // Rotate Left Decimal
        RRD,    // Rotate Right Decimal

        // Flow control
        NOP,    // No Operation
        JP,     // Jump
        JR,     // Jump Relative
        CALL,   // Call
        CALLR,  // Call Relative
        RET,    // Return
        RETI,   // Return from Interrupt
        RETN,   // Return from Non-Maskable Interrupt
        DJNZ,   // Decrement, Jump if Not Zero
        JANZ,   // Jump if A is Not Zero
        RST,    // Software Interrupt
        SCF,    // Set Carry Flag
        CCF,    // Complement Carry Flag

        // System
        EI,     // Enable Interrupts
        DI,     // Disable Interrupts
        IM,     // Interrupt Mode
        HALT,   // Halt

        // Pseudo-Ops
        DB,     // Define Byte
        DW,     // Define Word
        DD,     // Define Double Word
        DS,     // Define Space
        EQU,    // Equals
        ORG,    // Org
    }

    public enum TokenType
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        LParen,
        RParen,
        End,
    }
}
