using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentPostingHandlerNotConfiguredException(Guid documentId, string typeCode)
    : NgbConfigurationException(message: $"Document type '{typeCode}' has no posting handler configured.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["documentId"] = documentId,
            ["typeCode"] = typeCode
        })
{
    public const string Code = "doc.posting.handler.not_configured";

    public Guid DocumentId { get; } = documentId;
    public string TypeCode { get; } = typeCode;
}
