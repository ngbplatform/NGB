using NGB.Metadata.Documents.Actions;

namespace NGB.Definitions.Documents.Actions;

/// <summary>
/// Binds immutable action metadata to optional runtime extension types.
/// </summary>
public sealed record DocumentActionDefinition(
    string DocumentTypeCode,
    DocumentActionMetadata Metadata,
    Type? HandlerType = null,
    Type? AvailabilityEvaluatorType = null,
    Type? AuthorizationEvaluatorType = null,
    string? DerivationCode = null);
