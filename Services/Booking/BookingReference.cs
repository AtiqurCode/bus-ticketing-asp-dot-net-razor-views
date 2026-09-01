using System.Security.Cryptography;

namespace BusTicketing.Services.Bookings;

public static class BookingReference
{
    // No 0/O/1/I to keep it readable over the phone.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New(string prefix, int length = 6)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"{prefix}-{new string(chars)}";
    }
}
