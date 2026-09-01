namespace NGB.Persistence.AuditLog;

/// <summary>
/// Provider-neutral read model used by audit health orchestration.
/// </summary>
public sealed class AuditHealthSnapshot
{
    public long EventsTrigger { get; init; }
    public long ChangesTrigger { get; init; }
    public long OrphanChanges { get; init; }
    public long EventsCount { get; init; }
    public DateTime? MinOccurredAtUtc { get; init; }
    public DateTime? MaxOccurredAtUtc { get; init; }
}

public interface IAuditHealthReader
{
    Task<AuditHealthSnapshot> ReadAsync(CancellationToken ct = default);
}
