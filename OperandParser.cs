namespace im8000asm;

internal class OperandParser
{
    public static Operand Parse(string text)
    {
        var operand = new Operand();
        var expressionHandler = new ExpressionHandler();

        // Is this indirect?
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            operand.Indirect = true;
            text = text[1..^1];
        }

        // Is this a register?
        if (Constants.RegisterTargetNames.Contains(text) && Enum.TryParse(text, out Constants.RegisterTarget reg))
        {
            operand.Register = reg;
            return operand;
        }

        // Is this an indexed access?
        foreach (Constants.RegisterTarget r in Enum.GetValues<Constants.RegisterTarget>())
        {
            string name = r.ToString();
            if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                string rest = text[name.Length..];
                if (rest.StartsWith('+') || rest.StartsWith('-'))
                {
                    operand.Register = r;
                    operand.Displacement = expressionHandler.Parse(rest);
                    return operand;
                }
            }
        }

        // Otherwise, this is an expression
        operand.Expression = expressionHandler.Parse(text);

        return operand;
    }
}
