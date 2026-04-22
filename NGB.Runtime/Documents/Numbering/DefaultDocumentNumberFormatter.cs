using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Numbering;

/// <summary>
/// Default numbering format: PREFIX-YYYY-000001
/// PREFIX is derived from the business segment of the document type code
/// by taking the first letter of each snake_case token.
/// Examples:
/// - "general_journal_entry" => "GJE"
/// - "demo.receivable_charge" => "RC"
/// </summary>
public sealed class DefaultDocumentNumberFormatter : IDocumentNumberFormatter
{
    public string Format(string typeCode, int fiscalYear, long sequence)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentRequiredException(nameof(typeCode));

        if (fiscalYear is < 1900 or > 3000)
            throw new NgbArgumentOutOfRangeException(nameof(fiscalYear), fiscalYear, "FiscalYear out of range.");

        if (sequence <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence must be positive.");

        var prefix = BuildPrefix(typeCode);
        return $"{prefix}-{fiscalYear}-{sequence:000000}";
    }

    private static string BuildPrefix(string typeCode)
    {
        // Keep it deterministic and stable. Do not depend on localized strings.
        var normalized = typeCode.Trim();
        var lastDot = normalized.LastIndexOf('.');
        if (lastDot >= 0 && lastDot + 1 < normalized.Length)
            normalized = normalized[(lastDot + 1)..];

        var parts = normalized
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return "DOC";

        Span<char> buf = stackalloc char[Math.Min(parts.Length, 8)];
        var j = 0;

        foreach (var p in parts)
        {
            if (j >= buf.Length)
                break;

            var c = p[0];
            buf[j++] = char.ToUpperInvariant(c);
        }

        return new string(buf[..j]);
    }
}
