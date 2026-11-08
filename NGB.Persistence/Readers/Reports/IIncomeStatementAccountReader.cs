using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Optional statement-reader capability for profit-and-loss activity excluding fiscal-year closing entries.
/// Opening and closing balances retain the ordinary accounting meaning.
/// </summary>
public interface IIncomeStatementAccountReader : IAccountingStatementAccountReader
{
    IAsyncEnumerable<AccountingStatementAccount> ReadIncomeStatementAsync(
        DateOnly from,
        DateOnly to,
        DimensionScopeBag? scopes,
        CancellationToken ct);
}
