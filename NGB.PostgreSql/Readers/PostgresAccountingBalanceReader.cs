using Dapper;
using NGB.Accounting.Balances;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingBalanceReader(
    IUnitOfWork uow,
    IDimensionSetReader dimensionSets)
    : IAccountingBalanceReader
{
    private sealed class Row
    {
        public DateOnly Period { get; init; }
        public Guid AccountId { get; init; }
        public Guid DimensionSetId { get; init; }
        public string? AccountCode { get; init; }
        public decimal OpeningBalance { get; init; }
        public decimal ClosingBalance { get; init; }
    }

    public async Task<IReadOnlyList<AccountingBalance>> GetForPeriodAsync(
        DateOnly period,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               b.period AS Period,
                               b.account_id AS AccountId,
                               b.dimension_set_id AS DimensionSetId,
                               a.code AS AccountCode,
                               b.opening_balance AS OpeningBalance,
                               b.closing_balance AS ClosingBalance
                           FROM accounting_balances b
                           JOIN accounting_accounts a ON a.account_id = b.account_id AND a.is_deleted = FALSE
                           WHERE b.period = @Period
                           ORDER BY b.account_id, b.dimension_set_id;
                           """;

        return await QueryRowsAsync(sql, new { Period = period }, ct);
    }

    public async Task<IReadOnlyList<AccountingBalance>> GetLatestClosedAsync(
        DateOnly period,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT MAX(period)
                           FROM accounting_closed_periods
                           WHERE period <= @Period;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        var closed = await uow.Connection.ExecuteScalarAsync<DateOnly?>(cmd);
        if (closed is null)
            return [];

        return await GetForPeriodAsync(closed.Value, ct);
    }

    private async Task<IReadOnlyDictionary<Guid, DimensionBag>> ResolveBagsAsync(
        IReadOnlyList<Row> rows,
        CancellationToken ct)
    {
        var ids = rows
            .Select(x => x.DimensionSetId)
            .Distinct()
            .ToArray();

        return await dimensionSets.GetBagsByIdsAsync(ids, ct);
    }

    private async Task<IReadOnlyList<AccountingBalance>> QueryRowsAsync(
        string sql,
        object args,
        CancellationToken ct)
    {
        var cmd = new CommandDefinition(
            sql,
            args,
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);

        var rows = (await uow.Connection.QueryAsync<Row>(cmd)).AsList();
        if (rows.Count == 0)
            return [];

        var bags = await ResolveBagsAsync(rows, ct);

        return rows.Select(r => new AccountingBalance
        {
            Period = r.Period,
            AccountId = r.AccountId,
            DimensionSetId = r.DimensionSetId,
            Dimensions = bags.TryGetValue(r.DimensionSetId, out var bag) ? bag : DimensionBag.Empty,
            AccountCode = r.AccountCode,
            OpeningBalance = r.OpeningBalance,
            ClosingBalance = r.ClosingBalance
        }).ToList();
    }
}
