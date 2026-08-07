using System.Text.Json.Serialization;
using NGB.Core.Documents;

namespace NGB.Application.Abstractions.IntegrationEvents;

/// <summary>
/// Versioned integration contract emitted after a document action commits in the document transaction.
/// </summary>
public sealed record DocumentActionCompletedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    string Source,
    string Subject,
    Guid? ActorUserId,
    Guid CorrelationId,
    Guid? CausationId,
    DocumentActionCompletedDataV1 Data)
{
    public const string EventType = "ngb.document.action.completed";
    public const int SchemaVersion = 1;

    [JsonPropertyName("type")]
    public string Type => EventType;

    [JsonPropertyName("schemaVersion")]
    public int PayloadSchemaVersion => SchemaVersion;
}

public sealed record DocumentActionCompletedDataV1(
    Guid DocumentId,
    string DocumentType,
    string ActionCode,
    DocumentStatus PreviousStatus,
    DocumentStatus CurrentStatus,
    long DocumentVersion);
