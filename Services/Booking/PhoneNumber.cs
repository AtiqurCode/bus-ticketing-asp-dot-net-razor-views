using System.Text.RegularExpressions;

namespace BusTicketing.Services.Bookings;

/// <summary>Normalises Bangladeshi mobile numbers to the local <c>01XXXXXXXXX</c> form.</summary>
public static partial class PhoneNumber
{
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var digits = DigitsOnly().Replace(input, "");

        digits = digits switch
        {
            { Length: 13 } when digits.StartsWith("880") => digits[2..],   // 8801XXXXXXXXX -> 01XXXXXXXXX
            { Length: 10 } when digits.StartsWith("1") => "0" + digits,      // 1XXXXXXXXX   -> 01XXXXXXXXX
            _ => digits
        };

        return IsValid(digits) ? digits : null;
    }

    public static bool IsValid(string? local) =>
        local is { Length: 11 } && local.StartsWith("01") && local.All(char.IsDigit);

    [GeneratedRegex("[^0-9]")]
    private static partial Regex DigitsOnly();
}
