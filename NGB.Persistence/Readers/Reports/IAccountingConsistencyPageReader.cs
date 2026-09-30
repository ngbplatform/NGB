namespace NGB.Persistence.Readers.Reports;

public sealed record AccountingConsistencyKey(string Code, Guid AccountId, Guid DimensionSetId);

public sealed record AccountingConsistencyPage(
    IReadOnlyList<AccountingConsistencySnapshotRow> Rows,
    bool HasMore,
    AccountingConsistencyKey? Next,
    bool HasBalances);

public sealed record AccountingConsistencyCounts(long Mismatch, long Missing, long Chain);

public interface IAccountingConsistencyPageReader
{
    Task<AccountingConsistencyPage> ReadPageAsync(
        DateOnly period,
        DateOnly? previous,
        AccountingConsistencyKey? after,
        int limit,
        CancellationToken ct);

    Task<AccountingConsistencyCounts> CountAsync(DateOnly period, DateOnly? previous, CancellationToken ct);
}
