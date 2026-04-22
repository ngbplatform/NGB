using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

/// <summary>
/// Thrown when a document posting handler is configured incorrectly in definitions/DI.
/// 
/// Examples:
/// - Handler type does not implement required contract.
/// - Handler TypeCode does not match the document type code.
/// - Handler type is not registered in DI.
/// - Multiple handler implementations are registered.
/// </summary>
public sealed class DocumentPostingHandlerMisconfiguredException(
    string documentTypeCode,
    string postingKind,
    string reason,
    string postingHandlerType,
    object? details = null,
    Exception? innerException = null)
    : NgbConfigurationException(
        message:
        $"Invalid {postingKind} posting handler configuration for document type '{documentTypeCode}': {reason}",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["postingKind"] = postingKind,
            ["documentTypeCode"] = documentTypeCode,
            ["postingHandlerType"] = postingHandlerType,
            ["reason"] = reason,
            ["details"] = details,
        },
        innerException: innerException)
{
    public const string Code = "doc.posting.handler.misconfigured";
}
