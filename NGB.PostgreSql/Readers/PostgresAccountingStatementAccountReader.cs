using System.Runtime.CompilerServices;
using Dapper;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingStatementAccountReader(IUnitOfWork uow) : IAccountingStatementAccountReader
{
    public async IAsyncEnumerable<AccountingStatementAccount> ReadAsync(
        DateOnly from,
        DateOnly to,
        DimensionScopeBag? scopes,
        [EnumeratorCancellation] CancellationToken ct)
    {
        from.EnsureMonthStart(nameof(from));
        to.EnsureMonthStart(nameof(to));

        if (to < from)
            throw new NgbArgumentInvalidException(nameof(to), "To must be on or after From.");

        var (ids, values, count) = SqlDimensionFilter.NormalizeScopes(scopes);

        await uow.EnsureConnectionOpenAsync(ct);
        await using var reader = await uow.Connection.ExecuteReaderAsync(new CommandDefinition(
            Sql,
            new { FromInclusive = from, ToInclusive = to, DimensionIds = ids, ValueIds = values, DimensionCount = count },
            uow.Transaction, commandTimeout: 300, cancellationToken: ct));

        while (await reader.ReadAsync(ct))
        {
            yield return new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                (StatementSection)reader.GetInt16(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7));
        }
    }

    // Statements use two authoritative month-end endpoints. Range turnovers remain separate for P&L.
    // In particular a closed To month uses its closing snapshot, not a reconstruction from its opening.
    private const string Sql = """
        WITH latest AS (
            SELECT MAX(period) FILTER (WHERE period < @FromInclusive) AS opening,
                   MAX(period) FILTER (WHERE period <= @ToInclusive) AS closing FROM accounting_closed_periods
        ), requested AS (
            SELECT * FROM unnest(@DimensionIds::uuid[], @ValueIds::uuid[]) AS p(dimension_id,value_id)
        ), matching AS (
            SELECT di.dimension_set_id FROM platform_dimension_set_items di JOIN requested p USING(dimension_id,value_id)
            GROUP BY di.dimension_set_id HAVING COUNT(DISTINCT di.dimension_id)=@DimensionCount
        ), amounts AS (
            SELECT b.account_id, b.closing_balance AS opening, 0::numeric AS debit, 0::numeric AS credit, 0::numeric AS closing
            FROM accounting_balances b CROSS JOIN latest l
            WHERE b.period=l.opening AND (@DimensionCount=0 OR b.dimension_set_id IN (SELECT dimension_set_id FROM matching))
            UNION ALL
            SELECT b.account_id, 0, 0, 0, b.closing_balance FROM accounting_balances b CROSS JOIN latest l
            WHERE b.period=l.closing AND (@DimensionCount=0 OR b.dimension_set_id IN (SELECT dimension_set_id FROM matching))
            UNION ALL
            SELECT t.account_id,
                CASE WHEN t.period<@FromInclusive AND (l.opening IS NULL OR t.period>l.opening) THEN t.debit_amount-t.credit_amount ELSE 0 END,
                CASE WHEN t.period>=@FromInclusive THEN t.debit_amount ELSE 0 END,
                CASE WHEN t.period>=@FromInclusive THEN t.credit_amount ELSE 0 END,
                CASE WHEN l.closing IS NULL OR t.period>l.closing THEN t.debit_amount-t.credit_amount ELSE 0 END
            FROM accounting_turnovers t CROSS JOIN latest l
            WHERE t.period<=@ToInclusive
              AND (t.period>=@FromInclusive OR l.opening IS NULL OR t.period>l.opening OR l.closing IS NULL OR t.period>l.closing)
              AND (@DimensionCount=0 OR t.dimension_set_id IN (SELECT dimension_set_id FROM matching))
        )
        SELECT a.account_id,a.code,a.name,a.statement_section,SUM(v.opening),SUM(v.debit),SUM(v.credit),SUM(v.closing)
        FROM amounts v JOIN accounting_accounts a ON a.account_id=v.account_id AND a.is_deleted=FALSE
        GROUP BY a.account_id,a.code,a.name,a.statement_section
        ORDER BY a.statement_section,a.code COLLATE "C",a.account_id;
        """;
}
