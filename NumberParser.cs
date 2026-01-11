using System.Globalization;

namespace im8000asm;

internal class NumberParser
{
    public static bool TryParseNumber(string str, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(str))
        {
            return false;
        }

        str = str.Replace("_", "");

        NumberStyles style = NumberStyles.Integer;

        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            style = NumberStyles.HexNumber;
            str = str[2..];
        }
        else if (str.StartsWith('$'))
        {
            style = NumberStyles.HexNumber;
            str = str[1..];
        }
        else if (str.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            style = NumberStyles.HexNumber;
            str = str[..^1];
        }
        else if (str.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            style = NumberStyles.BinaryNumber;
            str = str[2..];
        }
        else if (str.StartsWith('%'))
        {
            style = NumberStyles.BinaryNumber;
            str = str[1..];
        }
        else if (str.EndsWith("b", StringComparison.OrdinalIgnoreCase))
        {
            style = NumberStyles.BinaryNumber;
            str = str[..^1];
        }

        return int.TryParse(str, style, CultureInfo.InvariantCulture, out value);
    }
}
