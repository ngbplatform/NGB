using Dapper;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingSummaryPageReader(IUnitOfWork uow) : IAccountingSummaryPageReader
{
    public async Task<IReadOnlyList<AccountingSummaryValue>> ReadGroupsAsync(
        AccountingSummaryQuery query,
        CancellationToken ct)
    {
        var sql = Source(query.Kind) + """
            SELECT grp::integer AS "Group", '00000000-0000-0000-0000-000000000000'::uuid AS "Id", '' AS "Code", '' AS "Name",
                SUM(opening) AS "Opening", SUM(debit) AS "Debit", SUM(credit) AS "Credit", SUM(closing) AS "Closing"
            FROM report_accounts GROUP BY grp ORDER BY grp
            """;

        await uow.EnsureConnectionOpenAsync(ct);

        return (await uow.Connection.QueryAsync<AccountingSummaryValue>(new(
                sql, Parameters(query),
                uow.Transaction, cancellationToken: ct)))
            .AsList();
    }

    public async Task<AccountingSummaryPage> ReadAccountsAsync(
        AccountingSummaryQuery query,
        int group,
        AccountingSummaryKey? after,
        int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var parameters = Parameters(query);
        parameters.Add("Group", group);
        parameters.Add("Limit", limit + 1);
        var seek = "";

        if (after is not null)
        {
            parameters.Add("AfterCode", after.Code);
            parameters.Add("AfterId", after.Id);
            seek = " AND (code COLLATE \"C\", id) > (@AfterCode COLLATE \"C\", @AfterId)";
        }

        var sql = Source(query.Kind) + """
            SELECT grp::integer AS "Group", id AS "Id", code AS "Code", name AS "Name", opening AS "Opening",
                debit AS "Debit", credit AS "Credit", closing AS "Closing"
            FROM report_accounts WHERE grp = @Group
            """ + seek + " ORDER BY code COLLATE \"C\", id LIMIT @Limit";

        await uow.EnsureConnectionOpenAsync(ct);

        var rows = (await uow.Connection.QueryAsync<AccountingSummaryValue>(new(
                sql,
                parameters,
                uow.Transaction,
                cancellationToken: ct)))
            .AsList();

        var more = rows.Count > limit;
        if (more)
            rows.RemoveAt(rows.Count - 1);

        return new(rows, more, more ? new(rows[^1].Code, rows[^1].Id) : null);
    }

    private static DynamicParameters Parameters(AccountingSummaryQuery query)
    {
        var (ids, values, count) = SqlDimensionFilter.NormalizeScopes(query.Scopes);

        return new(new
        {
            FromInclusive = query.From,
            ToInclusive = query.To,
            DimensionIds = ids,
            ValueIds = values,
            DimensionCount = count
        });
    }

    private static string Source(AccountingSummaryKind kind)
    {
        if (kind == AccountingSummaryKind.TrialBalance)
        {
            return "WITH account_source(id,code,name,grp,opening,debit,credit,section) AS (" + PostgresAccountSummarySql.Sql
                + "), report_accounts AS (SELECT *, opening+debit-credit AS closing FROM account_source) ";
        }

        var filter = kind switch
        {
            AccountingSummaryKind.BalanceSheet => "(grp BETWEEN 1 AND 3 AND closing<>0) OR grp>=4",
            AccountingSummaryKind.IncomeStatement => "grp>=4 AND debit<>credit",
            AccountingSummaryKind.Equity => "(grp=3 AND (opening<>0 OR closing<>0)) OR grp>=4",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        return "WITH account_source(id,code,name,grp,opening,debit,credit,closing) AS (" + PostgresAccountingStatementAccountReader.Sql
            + "), report_accounts AS (SELECT * FROM account_source WHERE " + filter + ") ";
    }
}
