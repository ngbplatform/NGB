using NGB.Accounting.Periods;

namespace NGB.Persistence.Readers.Periods;

/// <summary>
/// Reader for month closing audit information.
/// </summary>
public interface IClosedPeriodReader
{
    /// <summary>
    /// Returns closed periods within the given inclusive range.
    /// </summary>
    Task<IReadOnlyList<ClosedPeriodRecord>> GetClosedAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default);

    Task<DateOnly?> GetLatestClosedPeriodAsync(CancellationToken ct = default);

    Task<bool> ExistsClosedAfterAsync(DateOnly period, CancellationToken ct = default);
}
