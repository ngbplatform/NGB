using NGB.Metadata.Base;

namespace NGB.Runtime.Validation;

internal static class ValidationMessageFormatter
{
    public static string ToLabel(string key, ColumnType type)
    {
        if (string.IsNullOrWhiteSpace(key))
            return key;

        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Remove technical suffixes for user-facing labels.
        if (parts.Count > 0 && string.Equals(parts[^1], "utc", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(parts.Count - 1);

        if (parts.Count > 0
            && type == ColumnType.Guid
            && string.Equals(parts[^1], "id", StringComparison.OrdinalIgnoreCase))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        if (parts.Count == 0)
            return key;

        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public static string RequiredFieldMessage(string label)
        => $"{label} is required.";

    public static string InvalidValueMessage(string label, ColumnType type)
        => type switch
        {
            ColumnType.Guid => $"Select a valid {label}.",
            ColumnType.Date => $"Enter a valid date for {label}.",
            ColumnType.DateTimeUtc => $"Enter a valid date and time for {label}.",
            ColumnType.Int32 or ColumnType.Int64 or ColumnType.Decimal => $"Enter a valid number for {label}.",
            _ => $"Enter a valid value for {label}."
        };
}
