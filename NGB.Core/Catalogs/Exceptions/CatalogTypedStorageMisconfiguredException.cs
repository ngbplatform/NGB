using NGB.Tools.Exceptions;

namespace NGB.Core.Catalogs.Exceptions;

public sealed class CatalogTypedStorageMisconfiguredException(
    string catalogCode,
    string reason,
    object? details = null,
    Exception? innerException = null)
    : NgbConfigurationException(
        message: CreateMessage(catalogCode, reason),
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["catalogCode"] = catalogCode,
            ["reason"] = reason,
            ["details"] = details,
        },
        innerException: innerException)
{
    public const string Code = "catalog.typed_storage.misconfigured";

    public string CatalogCode { get; } = catalogCode;
    public string Reason { get; } = reason;

    private static string CreateMessage(string catalogCode, string reason)
        => reason switch
        {
            "typed_storage_duplicate_catalog_code" =>
                $"Catalog typed storage is misconfigured. Multiple typed storages are registered for catalog '{catalogCode}' (duplicate same key).",
            "typed_storage_not_registered_in_di" =>
                $"Catalog typed storage is misconfigured. Catalog '{catalogCode}' typed storage is not registered in DI.",
            "typed_storage_multiple_matches" =>
                $"Catalog typed storage is misconfigured. Catalog '{catalogCode}' typed storage has multiple matching registrations.",
            "typed_storage_catalog_code_mismatch" =>
                $"Catalog typed storage is misconfigured. Catalog '{catalogCode}' typed storage CatalogCode does not match the definition.",
            "typed_storage_must_implement_contract" =>
                $"Catalog typed storage is misconfigured. Catalog '{catalogCode}' typed storage must implement ICatalogTypeStorage.",
            _ =>
                $"Catalog typed storage is misconfigured. catalogCode='{catalogCode}', reason='{reason}'."
        };
}
