using NGB.Tools.Exceptions;

namespace NGB.Core.Catalogs.Exceptions;

public sealed class CatalogSchemaValidationException(string diagnostics) : NgbConfigurationException(
    message: diagnostics,
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["area"] = "Catalogs"
    })
{
    public const string Code = "catalog.schema.validation_failed";

    public string Diagnostics { get; } = diagnostics;
}
