using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentTypeNotFoundException(string typeCode) : NgbNotFoundException(
    message: $"Unknown document type '{typeCode}'.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["typeCode"] = typeCode
    })
{
    public const string Code = "doc.type.not_found";

    public string TypeCode { get; } = typeCode;
}
