using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Universal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents;

public static class DocumentMetadataRuntimeExtensions
{
    public static DocumentHeadDescriptor CreateHeadDescriptor(this DocumentTypeMetadata meta)
    {
        if (meta is null)
            throw new NgbArgumentRequiredException(nameof(meta));

        var headTable = meta.Tables.FirstOrDefault(table => table.Kind == TableKind.Head)
            ?? throw new NgbConfigurationViolationException($"Document '{meta.TypeCode}' has no head table.");

        var displayColumn = headTable.Columns
            .FirstOrDefault(column => string.Equals(column.ColumnName, "display", StringComparison.OrdinalIgnoreCase))
            ?.ColumnName
            ?? throw new NgbConfigurationViolationException($"Document '{meta.TypeCode}' must define a display column.");

        return new DocumentHeadDescriptor(
            TypeCode: meta.TypeCode,
            HeadTableName: headTable.TableName,
            DisplayColumn: displayColumn,
            Columns: headTable.Columns
                .Where(column => !string.Equals(column.ColumnName, "document_id", StringComparison.OrdinalIgnoreCase))
                .Select(column => new DocumentHeadColumn(column.ColumnName, column.Type))
                .ToArray());
    }

    public static DocumentTableMetadata GetRequiredPartTable(this DocumentTypeMetadata meta, string partCode)
    {
        if (meta is null)
            throw new NgbArgumentRequiredException(nameof(meta));

        if (string.IsNullOrWhiteSpace(partCode))
            throw new NgbArgumentRequiredException(nameof(partCode));

        return meta.Tables.FirstOrDefault(table =>
                   table.Kind == TableKind.Part
                   && string.Equals(table.GetRequiredPartCode(meta.TypeCode), partCode.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new NgbConfigurationViolationException(
                   $"Document '{meta.TypeCode}' does not define part '{partCode.Trim()}'.");
    }
}
