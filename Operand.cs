namespace im8000asm;

internal class Operand
{
    public Constants.RegisterTarget? Register { get; set; }
    public bool Indirect { get; set; }
    public ExpressionHandler.Expression? Expression { get; set; }
    public ExpressionHandler.Expression? Displacement { get; set; }
}
