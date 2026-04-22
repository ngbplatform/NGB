using System.Security.Cryptography;
using System.Text;
using NGB.Tools.Exceptions;

namespace NGB.Tools.Normalization;

/// <summary>
/// Shared normalization utilities for *unquoted* SQL identifiers.
///
/// This helper intentionally normalizes to a conservative ASCII subset to avoid quoting.
/// Provider-specific constraints (like maximum identifier length) must be supplied by callers.
/// </summary>
public static class IdentifierNormalization
{
    // '_' + 12 hex chars.
    public const int HashSuffixLen = 13;

    /// <summary>
    /// Strict token normalization suitable for building SQL identifiers.
    ///
    /// Rules:
    /// - lower
    /// - non [a-z0-9] => '_'
    /// - collapse multiple '_' into one
    /// - trim '_' from both ends
    /// </summary>
    public static string NormalizeStrictToken(string? code, string paramName, string emptyResultMessage)
    {
        if (code is null)
            throw new NgbArgumentRequiredException(paramName);

        code = code.Trim();
        if (code.Length == 0)
            throw new NgbArgumentInvalidException(paramName, "Code must be non-empty.");

        var sb = new StringBuilder(code.Length);
        foreach (var ch in code.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        // Collapse consecutive underscores.
        var collapsed = new StringBuilder(sb.Length);
        var prevUnderscore = false;

        foreach (var ch in sb.ToString())
        {
            if (ch == '_')
            {
                if (prevUnderscore)
                    continue;

                prevUnderscore = true;
                collapsed.Append(ch);
                continue;
            }

            prevUnderscore = false;
            collapsed.Append(ch);
        }

        var result = collapsed.ToString().Trim('_');
        if (result.Length == 0)
            throw new NgbArgumentInvalidException(paramName, emptyResultMessage);

        return result;
    }

    /// <summary>
    /// Ensures the token fits into the given max length. If truncation is required,
    /// appends a deterministic MD5-based suffix to reduce collisions.
    /// </summary>
    public static string LimitWithHashSuffix(string token, int maxLen)
    {
        if (token.Length <= maxLen)
            return token;

        var prefixLen = maxLen - HashSuffixLen;
        if (prefixLen < 1)
            throw new NgbInvariantViolationException(
                $"Invalid maxLen={maxLen} for hash suffix length {HashSuffixLen}.",
                new Dictionary<string, object?>
                {
                    ["maxLen"] = maxLen,
                    ["hashSuffixLen"] = HashSuffixLen
                });

        var prefix = token[..prefixLen];
        var hash = Hash12Hex(token);
        return $"{prefix}_{hash}";
    }

    /// <summary>
    /// Normalizes a free-form code into a safe *unquoted* SQL identifier token and limits it
    /// for usage as a table code (part of a table name).
    /// </summary>
    public static string NormalizeStrictTableCode(
        string? code,
        string paramName,
        string emptyResultMessage,
        int maxTableCodeLen)
    {
        var token = NormalizeStrictToken(code, paramName, emptyResultMessage);
        return LimitWithHashSuffix(token, maxTableCodeLen);
    }

    /// <summary>
    /// Normalizes a free-form code into a safe *unquoted* SQL identifier token and limits it
    /// for usage as a physical column name token.
    ///
    /// IMPORTANT: Unquoted PostgreSQL identifiers must start with [a-z_] (after normalization).
    /// If the normalized token starts with a digit, a deterministic prefix is prepended.
    /// </summary>
    public static string NormalizeStrictColumnCode(
        string? code,
        string paramName,
        string emptyResultMessage,
        int maxSqlIdentifierLen,
        string digitPrefix)
    {
        var token = NormalizeStrictToken(code, paramName, emptyResultMessage);

        if (char.IsDigit(token[0]))
            token = digitPrefix + token;

        return LimitWithHashSuffix(token, maxSqlIdentifierLen);
    }

    private static string Hash12Hex(string input)
    {
        // Keep MD5: deterministic, fast, and easy to match across layers when needed.
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..12];
    }
}
