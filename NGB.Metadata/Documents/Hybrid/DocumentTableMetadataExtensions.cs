using NGB.Metadata.Base;
using NGB.Tools.Exceptions;

namespace NGB.Metadata.Documents.Hybrid;

public static class DocumentTableMetadataExtensions
{
    public static string GetRequiredPartCode(this DocumentTableMetadata table, string documentTypeCode)
    {
        if (table is null)
            throw new NgbArgumentRequiredException(nameof(table));

        if (table.Kind != TableKind.Part)
        {
            throw new NgbConfigurationViolationException(
                $"Document '{documentTypeCode}' table '{table.TableName}' is not a part table and cannot expose PartCode.");
        }

        return NormalizeRequiredPartCode(
            table.PartCode,
            $"Document '{documentTypeCode}' part table '{table.TableName}'");
    }

    internal static string NormalizeRequiredPartCode(string? partCode, string location)
    {
        if (string.IsNullOrWhiteSpace(partCode))
            throw new NgbConfigurationViolationException($"{location} must declare a non-empty PartCode.");

        var trimmed = partCode.Trim();
        if (!string.Equals(trimmed, partCode, StringComparison.Ordinal))
            throw new NgbConfigurationViolationException($"{location} must declare a trimmed PartCode.");

        return trimmed;
    }
}
