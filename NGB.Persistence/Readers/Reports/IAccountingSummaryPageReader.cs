using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

public enum AccountingSummaryKind
{
    TrialBalance,
    BalanceSheet,
    IncomeStatement,
    Equity
}

public sealed record AccountingSummaryQuery(
    AccountingSummaryKind Kind,
    DateOnly From,
    DateOnly To,
    DimensionScopeBag? Scopes);

public sealed record AccountingSummaryKey(string Code, Guid Id);

public sealed record AccountingSummaryValue(
    int Group,
    Guid Id,
    string Code,
    string Name,
    decimal Opening,
    decimal Debit,
    decimal Credit,
    decimal Closing);

public sealed record AccountingSummaryPage(
    IReadOnlyList<AccountingSummaryValue> Rows,
    bool HasMore,
    AccountingSummaryKey? Next);

/// <summary>Aggregates a bounded set of sections, or selects one keyset page of accounts in a section.</summary>
public interface IAccountingSummaryPageReader
{
    Task<IReadOnlyList<AccountingSummaryValue>> ReadGroupsAsync(AccountingSummaryQuery query, CancellationToken ct);

    Task<AccountingSummaryPage> ReadAccountsAsync(
        AccountingSummaryQuery query,
        int group,
        AccountingSummaryKey? after,
        int limit,
        CancellationToken ct);
}
