using NGB.Accounting.Balances;

namespace NGB.Persistence.Writers;

public sealed record AccountingBalanceProjectionResult(
    int RowsWritten,
    long ForbiddenCount,
    long WarningCount,
    IReadOnlyList<NegativeBalanceViolation> ViolationSamples);

/// <summary>
/// Calculates and persists a monthly balance snapshot set-wise in the database.
/// </summary>
public interface IAccountingBalanceProjectionWriter
{
    Task<AccountingBalanceProjectionResult> ProjectAsync(
        DateOnly period,
        bool replaceExisting,
        CancellationToken ct = default);
}
