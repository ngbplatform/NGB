namespace NGB.Persistence.Periods;

public interface IClosedPeriodRepository
{
    Task<bool> IsClosedAsync(DateOnly period, CancellationToken ct = default);

    Task<DateOnly?> FindFirstClosedAsync(IReadOnlyCollection<DateOnly> periods, CancellationToken ct = default);

    /// <summary>
    /// Finds a closed month at or after the supplied month. Providers should implement a range query.
    /// The bounded calendar fallback preserves compatibility with existing persistence adapters.
    /// </summary>
    Task<DateOnly?> FindFirstClosedOnOrAfterAsync(DateOnly period, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var first = new DateOnly(period.Year, period.Month, 1);
        var count = (DateOnly.MaxValue.Year - first.Year) * 12 + 13 - first.Month;
        var months = new DateOnly[count];

        for (var index = 0; index < count; index++)
        {
            months[index] = first.AddMonths(index);
        }

        return FindFirstClosedAsync(months, ct);
    }

    Task MarkClosedAsync(DateOnly period, string closedBy, DateTime closedAtUtc, CancellationToken ct = default);

    Task ReopenAsync(DateOnly period, CancellationToken ct = default);
}
