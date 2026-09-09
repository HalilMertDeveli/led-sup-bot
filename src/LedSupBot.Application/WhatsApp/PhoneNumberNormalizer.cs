namespace LedSupBot.Application.WhatsApp;

/// <summary>Canonical Turkish/E.164 phone comparison without logging full numbers.</summary>
public static class PhoneNumberNormalizer
{
    /// <summary>
    /// Normalizes to country-prefixed digits (e.g. 905xxxxxxxxx) for safe equality checks.
    /// Returns null when input is not a dialable phone (e.g. @lid JIDs).
    /// </summary>
    public static string? TryNormalizeToCanonicalDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('@'))
        {
            var domain = trimmed.Split('@')[^1].Split(':')[0];
            if (!domain.Equals("s.whatsapp.net", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var user = trimmed.Split('@')[0]?.Split(':')[0];
            if (string.IsNullOrWhiteSpace(user) || !user.All(char.IsDigit))
            {
                return null;
            }

            trimmed = user;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        // 05xx xxx xx xx -> 905xx...
        if (digits.Length == 11 && digits.StartsWith('0'))
        {
            digits = "90" + digits[1..];
        }

        // 5xx... (10 digits) -> 905xx...
        if (digits.Length == 10 && digits.StartsWith('5'))
        {
            digits = "90" + digits;
        }

        // Already 90xxxxxxxxxx
        if (digits.Length == 12 && digits.StartsWith("90", StringComparison.Ordinal))
        {
            return digits;
        }

        // Other international numbers with reasonable length
        if (digits.Length is >= 10 and <= 15)
        {
            return digits;
        }

        return null;
    }

    public static bool CanonicalEquals(string? left, string? right)
    {
        var a = TryNormalizeToCanonicalDigits(left);
        var b = TryNormalizeToCanonicalDigits(right);
        return a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);
    }
}
