using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Actions;

public sealed class DocumentActionNotFoundException(string documentType, string actionCode)
    : NgbNotFoundException(
        $"Document action '{actionCode}' is not registered for '{documentType}'.",
        "document_action.not_found",
        new Dictionary<string, object?>
        {
            [StandardDocumentActionCodes.DocumentType] = documentType,
            [StandardDocumentActionCodes.DocumentActionCode] = actionCode
        });

public sealed class DocumentActionForbiddenException(string documentType, string actionCode)
    : NgbForbiddenException(
        "The current user is not allowed to execute this document action.",
        "document_action.forbidden",
        new Dictionary<string, object?>
        {
            [StandardDocumentActionCodes.DocumentType] = documentType,
            [StandardDocumentActionCodes.DocumentActionCode] = actionCode
        });

public sealed class DocumentActionUnavailableException(
    string documentType,
    string actionCode,
    IReadOnlyList<string> reasonCodes)
    : NgbConflictException(
        "The document action is not available in the current state.",
        "document_action.unavailable",
        new Dictionary<string, object?>
        {
            [StandardDocumentActionCodes.DocumentType] = documentType,
            [StandardDocumentActionCodes.DocumentActionCode] = actionCode,
            ["reasonCodes"] = reasonCodes
        });

public sealed class DocumentVersionConflictException(Guid documentId, long expectedVersion, long actualVersion)
    : NgbConflictException(
        "The document changed after it was loaded. Refresh and try again.",
        "document.version_conflict",
        new Dictionary<string, object?>
        {
            [StandardDocumentActionCodes.DocumentIdKey] = documentId,
            ["expectedVersion"] = expectedVersion,
            ["actualVersion"] = actualVersion
        });

public sealed class DocumentActionIdempotencyConflictException(string idempotencyKey)
    : NgbConflictException(
        "The idempotency key was already used for a different request.",
        "document_action.idempotency_conflict",
        new Dictionary<string, object?> { ["idempotencyKey"] = idempotencyKey });

public sealed class DocumentActionInProgressException(string idempotencyKey)
    : NgbConflictException(
        "An action with this idempotency key is still in progress.",
        "document_action.in_progress",
        new Dictionary<string, object?> { ["idempotencyKey"] = idempotencyKey });
