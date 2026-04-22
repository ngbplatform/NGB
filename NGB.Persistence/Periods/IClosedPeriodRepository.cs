namespace NGB.Persistence.Periods;

public interface IClosedPeriodRepository
{
    Task<bool> IsClosedAsync(DateOnly period, CancellationToken ct = default);

    Task MarkClosedAsync(DateOnly period, string closedBy, DateTime closedAtUtc, CancellationToken ct = default);

    Task ReopenAsync(DateOnly period, CancellationToken ct = default);
}
