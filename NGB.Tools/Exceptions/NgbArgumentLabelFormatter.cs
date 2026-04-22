namespace NGB.Tools.Exceptions;

public static class NgbArgumentLabelFormatter
{
    public static string Format(string? paramName)
    {
        if (string.IsNullOrWhiteSpace(paramName))
            return "Value";

        var raw = paramName.Trim();

        if (raw.Contains('.'))
            raw = raw.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];

        raw = raw
            .Replace("[]", " ", StringComparison.Ordinal)
            .Replace("[", " ", StringComparison.Ordinal)
            .Replace("]", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal);

        var spaced = InsertCamelCaseSpaces(raw);
        var parts = spaced
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        while (parts.Count > 1 && IsWrapperToken(parts[0]))
        {
            parts.RemoveAt(0);
        }

        while (parts.Count > 1 && IsTechnicalSuffix(parts[^1]))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        if (parts.Count == 0)
            return "Value";

        return string.Join(' ', parts.Select(FormatToken));
    }

    private static bool IsWrapperToken(string token)
        => token.Equals("request", StringComparison.OrdinalIgnoreCase)
           || token.Equals("payload", StringComparison.OrdinalIgnoreCase)
           || token.Equals("fields", StringComparison.OrdinalIgnoreCase)
           || token.Equals("parameters", StringComparison.OrdinalIgnoreCase)
           || token.Equals("filters", StringComparison.OrdinalIgnoreCase)
           || token.Equals("layout", StringComparison.OrdinalIgnoreCase);

    private static bool IsTechnicalSuffix(string token)
        => token.Equals("id", StringComparison.OrdinalIgnoreCase)
           || token.Equals("utc", StringComparison.OrdinalIgnoreCase)
           || token.Equals("inclusive", StringComparison.OrdinalIgnoreCase);

    private static string InsertCamelCaseSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var chars = new List<char>(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1])))
                chars.Add(' ');

            chars.Add(c);
        }

        return new string(chars.ToArray());
    }

    private static string FormatToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return token;

        return token.Length == 1
            ? token.ToUpperInvariant()
            : char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }
}
