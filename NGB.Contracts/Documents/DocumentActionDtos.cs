using System.Text.Json;
using NGB.Core.Documents.Actions;

namespace NGB.Contracts.Documents;

public sealed record DocumentActionDisabledReasonDto(string Code, string Message);

public sealed record DocumentActionConfirmationDto(
    DocumentActionConfirmationMode Mode,
    string Title,
    string Message,
    string ConfirmLabel);

public sealed record DocumentActionTargetDto(string Code, IReadOnlyDictionary<string, string?> Parameters);

public sealed record DocumentActionDto(
    string Code,
    string Label,
    string? LabelKey,
    string? Description,
    string? Icon,
    DocumentActionKind Kind,
    DocumentActionExecutionKind ExecutionKind,
    int Order,
    bool IsAllowed,
    IReadOnlyList<DocumentActionDisabledReasonDto> DisabledReasons,
    DocumentActionConfirmationDto? Confirmation,
    DocumentActionTargetDto? Target);

public sealed record DocumentEditorStateDto(
    Services.DocumentDto Document,
    long DocumentVersion,
    IReadOnlyList<DocumentActionDto> Actions);

public sealed record ExecuteDocumentActionRequestDto(
    long ExpectedVersion,
    JsonElement? Payload = null,
    string? Reason = null);

public sealed record ExecuteDocumentActionResultDto(
    Guid ExecutionId,
    string ActionCode,
    Services.DocumentDto Document,
    long DocumentVersion,
    IReadOnlyList<DocumentActionDto> Actions,
    bool WorkCenterMayChange,
    Services.DocumentDto? CreatedDocument = null);
