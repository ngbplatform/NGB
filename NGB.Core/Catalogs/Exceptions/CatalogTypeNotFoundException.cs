using NGB.Tools.Exceptions;

namespace NGB.Core.Catalogs.Exceptions;

public sealed class CatalogTypeNotFoundException(string catalogCode) : NgbNotFoundException(
    message: $"Unknown catalog code '{catalogCode}'.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["catalogCode"] = catalogCode
    })
{
    public const string Code = "catalog.type.not_found";

    public string CatalogCode { get; } = catalogCode;
}
