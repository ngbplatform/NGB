using System.Text.RegularExpressions;
using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Actions;

/// <summary>
/// Extensible, canonical identifier for a document action.
/// </summary>
public readonly partial record struct DocumentActionCode
{
    public const int MaxLength = 128;

    public DocumentActionCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new NgbArgumentInvalidException(nameof(value), "Document action code must be non-empty.");

        var normalized = value.Trim().ToLowerInvariant();
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new NgbArgumentInvalidException(
                nameof(value),
                "Document action code must already be trimmed and use canonical lowercase.");
        }

        if (normalized.Length > MaxLength)
            throw new NgbArgumentInvalidException(nameof(value), $"Document action code exceeds max length {MaxLength}.");

        if (!ActionCodePattern().IsMatch(normalized))
        {
            throw new NgbArgumentInvalidException(
                nameof(value),
                "Document action code may contain only lowercase letters, digits, '.', '_', ':', and '-'.");
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ActionCodePattern();
}
