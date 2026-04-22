using System.Text.RegularExpressions;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Internal;

/// <summary>
/// Centralized validation for identifiers used in dynamic SQL.
///
/// IMPORTANT:
/// - Dynamic tables/columns are used as UNQUOTED identifiers.
/// - In PostgreSQL, unquoted identifiers must match: ^[a-z_][a-z0-9_]*$
/// - PostgreSQL identifier length limit is 63 bytes (ASCII => 63 chars).
/// </summary>
internal static class PostgresSqlIdentifiers
{
    public const int MaxIdentifierLength = 63;

    private static readonly Regex SafeUnquoted = new(
        "^[a-z_][a-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void EnsureOrThrow(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new NgbConfigurationViolationException(
                $"Unsafe identifier (empty) in {context}.",
                new Dictionary<string, object?> { ["context"] = context, ["value"] = value });

        if (value.Length > MaxIdentifierLength)
            throw new NgbConfigurationViolationException(
                $"Unsafe identifier '{value}' (len={value.Length} > {MaxIdentifierLength}) in {context}.",
                new Dictionary<string, object?> { ["context"] = context, ["value"] = value, ["len"] = value.Length });

        if (!SafeUnquoted.IsMatch(value))
            throw new NgbConfigurationViolationException(
                $"Unsafe identifier '{value}' in {context}. Must match '{SafeUnquoted}'.",
                new Dictionary<string, object?> { ["context"] = context, ["value"] = value, ["regex"] = SafeUnquoted.ToString() });
    }
}
