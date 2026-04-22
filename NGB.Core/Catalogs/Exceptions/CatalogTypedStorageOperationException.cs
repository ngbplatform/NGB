using NGB.Tools.Exceptions;

namespace NGB.Core.Catalogs.Exceptions;

/// <summary>
/// Thrown when a per-catalog typed storage (cat_* table(s)) operation fails due to infrastructure reasons.
///
/// Notes:
/// - This is NOT a schema/configuration problem (see <see cref="CatalogTypedStorageMisconfiguredException"/>).
/// - This is a runtime/infrastructure failure (e.g., DDL/DML errors, timeouts, network failures).
/// </summary>
public sealed class CatalogTypedStorageOperationException(
    Guid catalogId,
    string catalogCode,
    string operation,
    object? details = null,
    Exception? innerException = null)
    : NgbInfrastructureException(
        message: "Catalog typed storage operation failed.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["catalogId"] = catalogId,
            ["catalogCode"] = catalogCode,
            ["operation"] = operation,
            ["details"] = details
        },
        innerException: innerException)
{
    public const string Code = "catalog.typed_storage.operation_failed";

    public Guid CatalogId { get; } = catalogId;
    public string CatalogCode { get; } = catalogCode;
    public string Operation { get; } = operation;
}
