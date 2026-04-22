using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentSchemaValidationException(string diagnostics) : NgbConfigurationException(
    message: diagnostics,
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["area"] = "Documents"
    })
{
    public const string Code = "doc.schema.validation_failed";

    public string Diagnostics { get; } = diagnostics;
}

