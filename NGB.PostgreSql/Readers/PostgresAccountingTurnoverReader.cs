using Dapper;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingTurnoverReader(
    IUnitOfWork uow,
    IDimensionSetReader dimensionSets)
    : IAccountingTurnoverReader
{
    private sealed class Row
    {
        public DateOnly Period { get; init; }
        public Guid AccountId { get; init; }
        public Guid DimensionSetId { get; init; }
        public string? AccountCode { get; init; }
        public decimal DebitAmount { get; init; }
        public decimal CreditAmount { get; init; }
    }

    public async Task<IReadOnlyList<AccountingTurnover>> GetForPeriodAsync(
        DateOnly period,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               t.period AS Period,
                               t.account_id AS AccountId,
                               t.dimension_set_id AS DimensionSetId,
                               a.code AS AccountCode,
                               t.debit_amount AS DebitAmount,
                               t.credit_amount AS CreditAmount
                           FROM accounting_turnovers t
                           JOIN accounting_accounts a ON a.account_id = t.account_id AND a.is_deleted = FALSE
                           WHERE t.period = @Period
                           ORDER BY t.account_id, t.dimension_set_id;
                           """;

        return await QueryRowsAsync(sql, new { Period = period }, ct);
    }

    public async Task<IReadOnlyList<AccountingTurnover>> GetRangeAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default)
    {
        const string sql = """
                           SELECT
                               t.period AS Period,
                               t.account_id AS AccountId,
                               t.dimension_set_id AS DimensionSetId,
                               a.code AS AccountCode,
                               t.debit_amount AS DebitAmount,
                               t.credit_amount AS CreditAmount
                           FROM accounting_turnovers t
                           JOIN accounting_accounts a ON a.account_id = t.account_id AND a.is_deleted = FALSE
                           WHERE t.period BETWEEN @From AND @To
                           ORDER BY t.period, t.account_id, t.dimension_set_id;
                           """;

        return await QueryRowsAsync(sql, new { From = fromInclusive, To = toInclusive }, ct);
    }

    private async Task<IReadOnlyList<AccountingTurnover>> QueryRowsAsync(
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

        return rows.Select(r => new AccountingTurnover
        {
            Period = r.Period,
            AccountId = r.AccountId,
            DimensionSetId = r.DimensionSetId,
            Dimensions = bags.TryGetValue(r.DimensionSetId, out var bag) ? bag : DimensionBag.Empty,
            AccountCode = r.AccountCode,
            DebitAmount = r.DebitAmount,
            CreditAmount = r.CreditAmount
        }).ToList();
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
}
