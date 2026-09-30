using Dapper;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingConsistencyPageReader(IUnitOfWork uow) : IAccountingConsistencyPageReader
{
    private const string Mismatch = "hascurrentbalancerow AND openingbalance+debitamount-creditamount<>closingbalance";
    private const string Missing = "@HasBalances AND hasturnoverrow AND NOT hascurrentbalancerow";
    private const string Chain = "@HasBalances AND @PreviousPeriod::date IS NOT NULL AND openingbalance<>previousclosingbalance";
    private static string Source => "WITH observations AS (" + PostgresAccountingConsistencySnapshotReader.SourceSql + ") ";

    public async Task<AccountingConsistencyPage> ReadPageAsync(
        DateOnly period,
        DateOnly? previous,
        AccountingConsistencyKey? after,
        int limit,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var hasBalances = await HasBalancesAsync(period, ct);
        var p = new DynamicParameters(new
        {
            Period = period,
            PreviousPeriod = previous,
            HasBalances = hasBalances,
            Limit = Math.Clamp(limit, 1, 500) + 1
        });
        var seek = "";

        if (after is not null)
        {
            p.Add("Code", after.Code);
            p.Add("Account", after.AccountId);
            p.Add("DimensionSet", after.DimensionSetId);
            seek = " AND (accountcode COLLATE \"C\",accountid,dimensionsetid) > (@Code COLLATE \"C\",@Account,@DimensionSet)";
        }

        var sql = Source + $"SELECT * FROM observations WHERE (({Mismatch}) OR ({Missing}) OR ({Chain}))" + seek
            + " ORDER BY accountcode COLLATE \"C\",accountid,dimensionsetid LIMIT @Limit";

        var rows = (await uow.Connection
                .QueryAsync<AccountingConsistencySnapshotRow>(new(sql, p, uow.Transaction, cancellationToken: ct)))
            .AsList();

        var more = rows.Count > limit;
        if (more)
            rows.RemoveAt(rows.Count - 1);

        var last = rows.LastOrDefault();

        return new(
            rows,
            more,
            more && last is not null
                ? new(last.AccountCode, last.AccountId, last.DimensionSetId)
                : null,
            hasBalances);
    }

    public async Task<AccountingConsistencyCounts> CountAsync(DateOnly period, DateOnly? previous, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        var hasBalances = await HasBalancesAsync(period, ct);
        var sql = Source + $"SELECT COUNT(*) FILTER (WHERE {Mismatch}) AS Mismatch, COUNT(*) FILTER (WHERE {Missing}) AS Missing, COUNT(*) FILTER (WHERE {Chain}) AS Chain FROM observations";

        return await uow.Connection.QuerySingleAsync<AccountingConsistencyCounts>(new(
            sql,
            new
            {
                Period = period,
                PreviousPeriod = previous,
                HasBalances = hasBalances
            },
            uow.Transaction,
            cancellationToken: ct));
    }

    private Task<bool> HasBalancesAsync(DateOnly period, CancellationToken ct)
        => uow.Connection.ExecuteScalarAsync<bool>(new(
        "SELECT EXISTS(SELECT 1 FROM accounting_balances b JOIN accounting_accounts a USING(account_id) WHERE b.period=@period AND NOT a.is_deleted)",
        new { period },
        uow.Transaction,
        cancellationToken: ct));
}
