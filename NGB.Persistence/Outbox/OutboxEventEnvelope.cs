namespace NGB.Persistence.Outbox;

/// <summary>
/// Provider-neutral persistence envelope for an integration event stored in the transactional outbox.
/// The payload is opaque to persistence providers and is decoded only by the owning integration adapter.
/// </summary>
public sealed record OutboxEventEnvelope(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTime OccurredAtUtc,
    string Source,
    string Subject,
    Guid? ActorUserId,
    Guid CorrelationId,
    Guid? CausationId,
    string PayloadJson,
    DateTime CreatedAtUtc);
