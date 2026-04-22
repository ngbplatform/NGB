using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Thrown when the generic document-actions endpoint is called before the platform-wide
/// document action dispatcher is implemented.
/// </summary>
public sealed class DocumentActionsNotSupportedException(string documentTypeCode, string actionCode)
    : NgbValidationException(
        message: $"Generic document actions are not supported for document type '{documentTypeCode}'.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["documentTypeCode"] = documentTypeCode,
            ["actionCode"] = actionCode,
        })
{
    public const string ErrorCodeConst = "documents.actions.not_supported";

    public string DocumentTypeCode { get; } = documentTypeCode;

    public string ActionCode { get; } = actionCode;
}
