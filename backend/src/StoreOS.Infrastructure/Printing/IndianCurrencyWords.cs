using System.Text;

namespace StoreOS.Infrastructure.Printing;

public static class IndianCurrencyWords
{
    private static readonly string[] Units =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    ];

    public static string Convert(decimal amount)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var isNegative = rounded < 0;
        var absVal = Math.Abs(rounded);

        var rupees = (long)Math.Truncate(absVal);
        var paise = (int)Math.Round((absVal - rupees) * 100, MidpointRounding.AwayFromZero);
        if (paise == 100)
        {
            rupees += 1;
            paise = 0;
        }

        var prefix = isNegative ? "Minus " : "";

        if (rupees == 0)
        {
            if (paise > 0)
                return $"{prefix}Rupees Zero and {ConvertLessThanThousand(paise)} Paise Only";
            return "Rupees Zero Only";
        }

        var words = new StringBuilder();

        var crores = rupees / 10000000;
        var rem = rupees % 10000000;

        var lakhs = rem / 100000;
        rem %= 100000;

        var thousands = rem / 1000;
        rem %= 1000;

        if (crores > 0)
            words.Append(ConvertLessThanThousand(crores)).Append(" Crore ");

        if (lakhs > 0)
            words.Append(ConvertLessThanThousand(lakhs)).Append(" Lakh ");

        if (thousands > 0)
            words.Append(ConvertLessThanThousand(thousands)).Append(" Thousand ");

        if (rem > 0)
            words.Append(ConvertLessThanThousand(rem));

        var result = words.ToString().Trim();

        var paisePart = paise > 0 ? $" and {ConvertLessThanThousand(paise)} Paise" : "";

        return $"{prefix}Rupees {result}{paisePart} Only";
    }

    private static string ConvertLessThanThousand(long num)
    {
        if (num == 0) return "";
        var sb = new StringBuilder();

        if (num >= 100)
        {
            sb.Append(Units[num / 100]).Append(" Hundred ");
            num %= 100;
        }

        if (num > 0)
        {
            if (num < 20)
            {
                sb.Append(Units[num]);
            }
            else
            {
                sb.Append(Tens[num / 10]);
                if (num % 10 > 0)
                    sb.Append(' ').Append(Units[num % 10]);
            }
        }

        return sb.ToString().Trim();
    }
}
