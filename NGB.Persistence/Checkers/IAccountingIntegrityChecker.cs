namespace NGB.Persistence.Checkers;

public interface IAccountingIntegrityChecker
{
    /// <summary>
    /// Checks that the accounting ledger for the period is consistent.
    /// Throws an exception if the integrity is compromised.
    /// </summary>
    Task AssertPeriodIsBalancedAsync(DateOnly period, CancellationToken ct = default);
}
