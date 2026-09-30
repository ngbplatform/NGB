using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

public interface IAccountingStatementAccountReader
{
    IAsyncEnumerable<AccountingStatementAccount> ReadAsync(
        DateOnly from,
        DateOnly to,
        DimensionScopeBag? scopes,
        CancellationToken ct);
}

public sealed record AccountingStatementAccount(
    Guid AccountId,
    string Code,
    string Name,
    StatementSection Section,
    decimal Opening,
    decimal Debit,
    decimal Credit,
    decimal Closing);
