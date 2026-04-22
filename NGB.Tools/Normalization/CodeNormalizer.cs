using NGB.Tools.Exceptions;

namespace NGB.Tools.Normalization;

/// <summary>
/// Centralized normalization rules for stable, persisted code keys.
///
/// Platform convention:
/// <c>code_norm = lower(trim(code))</c>.
///
/// Notes:
/// - This is a technical convention used for comparisons, unique constraints, and deterministic identifiers.
/// - It intentionally does not encode any domain/business meaning beyond normalization.
/// </summary>
public static class CodeNormalizer
{
    /// <summary>
    /// Normalizes a code to <c>code_norm = lower(trim(code))</c>.
    /// Throws NGB argument exceptions on null/empty.
    /// </summary>
    public static string NormalizeCodeNorm(string? code, string paramName)
    {
        if (code is null)
            throw new NgbArgumentRequiredException(paramName);

        code = code.Trim();
        if (code.Length == 0)
            throw new NgbArgumentInvalidException(paramName, "Code must be non-empty.");

        return code.ToLowerInvariant();
    }
}
