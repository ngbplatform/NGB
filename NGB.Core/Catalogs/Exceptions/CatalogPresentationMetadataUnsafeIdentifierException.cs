using NGB.Tools.Exceptions;

namespace NGB.Core.Catalogs.Exceptions;

public sealed class CatalogPresentationMetadataUnsafeIdentifierException(
    string catalogCode,
    string tableName,
    string displayColumn)
    : NgbConfigurationException(
        message: $"Unsafe table/column identifier in catalog presentation metadata for '{catalogCode}'.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["catalogCode"] = catalogCode,
            ["tableName"] = tableName,
            ["displayColumn"] = displayColumn,
        })
{
    public const string Code = "catalog.presentation.unsafe_identifier";

    public string CatalogCode { get; } = catalogCode;
    public string TableName { get; } = tableName;
    public string DisplayColumn { get; } = displayColumn;
}

