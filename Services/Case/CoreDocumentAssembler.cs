using System.Text;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Code-owned legal skeleton: numbering, incorporation paragraphs, caption, prayer, signature and memo header (CASE-05).
/// </summary>
public static partial class CoreDocumentAssembler
{
    private static readonly (int Value, string Numeral)[] RomanNumerals =
    {
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    };

    public static string ToRoman(int number)
    {
        if (number < 1 || number > 3999)
            throw new ArgumentOutOfRangeException(nameof(number), number, "Roman numerals support 1 to 3999");

        var sb = new StringBuilder();
        foreach (var (value, numeral) in RomanNumerals)
        {
            while (number >= value)
            {
                sb.Append(numeral);
                number -= value;
            }
        }

        return sb.ToString();
    }
}
