namespace NGB.PostgreSql.Search;

/// <summary>
/// Converts a canonical UUID prefix into an indexable UUID range. Short fragments are rejected
/// to avoid turning a lookup into an effectively unbounded scan.
/// </summary>
internal readonly record struct GuidSearchRange(Guid Lower, Guid Upper)
{
    internal const int MinimumHexCharacters = 8;

    internal static bool TryCreate(string? value, out GuidSearchRange range)
    {
        range = default;
        if (string.IsNullOrEmpty(value) || value.Length > 36)
            return false;

        Span<char> hex = stackalloc char[32];
        var hexLength = 0;

        for (var index = 0; index < value.Length; index++)
        {
            var hyphenExpected = index is 8 or 13 or 18 or 23;
            var character = value[index];

            if (hyphenExpected)
            {
                if (character != '-')
                    return false;

                continue;
            }

            if (!IsHex(character) || hexLength == hex.Length)
                return false;

            hex[hexLength++] = char.ToLowerInvariant(character);
        }

        if (hexLength < MinimumHexCharacters)
            return false;

        var lowerHex = new string(hex[..hexLength]).PadRight(32, '0');
        var upperHex = new string(hex[..hexLength]).PadRight(32, 'f');
        range = new GuidSearchRange(ParseHex(lowerHex), ParseHex(upperHex));

        return true;
    }

    private static bool IsHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static Guid ParseHex(string value)
        => Guid.ParseExact($"{value[..8]}-{value[8..12]}-{value[12..16]}-{value[16..20]}-{value[20..]}", "D");
}
