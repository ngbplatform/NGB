using NGB.Tools.Exceptions;

namespace NGB.Core.Catalogs.Exceptions;

public sealed class CatalogNotFoundException(Guid catalogId) : NgbNotFoundException(
    message: $"Catalog '{catalogId}' was not found.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["catalogId"] = catalogId
    })
{
    public const string Code = "catalog.not_found";

    public Guid CatalogId { get; } = catalogId;
}
