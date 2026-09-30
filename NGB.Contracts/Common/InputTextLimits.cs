using NGB.Tools.Exceptions;

namespace NGB.Contracts.Common;

public static class InputTextLimits
{
    public const int MaxSearchLength = 256;

    public static string? NormalizeSearch(string? value, string parameterName = "search")
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Length > MaxSearchLength)
        {
            throw new NgbArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"Search text can contain up to {MaxSearchLength} characters.");
        }

        return value.Trim();
    }
}
