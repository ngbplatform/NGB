namespace NGB.Persistence.Readers.Periods;

/// <summary>
/// Read model for accounting activity by month based on the ground-truth accounting register.
/// </summary>
public interface IAccountingPeriodActivityReader
{
    Task<DateOnly?> GetEarliestActivityPeriodAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DateOnly>> GetActivityPeriodsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default);
}
