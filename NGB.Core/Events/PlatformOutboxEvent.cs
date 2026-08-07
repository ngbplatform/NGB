namespace NGB.Core.Events;

public sealed record PlatformOutboxEvent(
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
