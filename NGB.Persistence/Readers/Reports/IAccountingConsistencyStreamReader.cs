namespace NGB.Persistence.Readers.Reports;

public interface IAccountingConsistencyStreamReader
{
    Task<bool> HasBalancesAsync(DateOnly period, CancellationToken ct);
    
    IAsyncEnumerable<IReadOnlyList<AccountingConsistencySnapshotRow>> ReadAsync(
        DateOnly period,
        DateOnly? previous,
        CancellationToken ct);
}
