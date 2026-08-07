using NGB.Core.Documents.Actions;

namespace NGB.Metadata.Documents.Actions;

public sealed record DocumentActionPresentation(
    string Label,
    string? LabelKey = null,
    string? Description = null,
    string? Icon = null);

public sealed record DocumentActionConfirmationMetadata(
    DocumentActionConfirmationMode Mode,
    string Title,
    string Message,
    string ConfirmLabel);

public sealed record DocumentActionTargetMetadata(string Code, IReadOnlyDictionary<string, string?> Parameters);

public sealed record DocumentActionMetadata(
    DocumentActionCode Code,
    DocumentActionPresentation Presentation,
    DocumentActionKind Kind,
    DocumentActionExecutionKind ExecutionKind,
    int Order,
    DocumentActionConfirmationMetadata? Confirmation = null,
    DocumentActionTargetMetadata? Target = null);
