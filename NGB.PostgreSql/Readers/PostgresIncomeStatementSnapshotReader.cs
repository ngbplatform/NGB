using Dapper;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL reader for Income Statement activity over a range.
/// Aggregates turnovers by account and applies dimension-scope filtering in SQL.
/// </summary>
public sealed class PostgresIncomeStatementSnapshotReader(IUnitOfWork uow) : IIncomeStatementSnapshotReader
{
    private static readonly short[] ProfitAndLossSections =
    [
        (short)StatementSection.Income,
        (short)StatementSection.CostOfGoodsSold,
        (short)StatementSection.Expenses,
        (short)StatementSection.OtherIncome,
        (short)StatementSection.OtherExpense
    ];

    private sealed class Row
    {
        public Guid AccountId { get; init; }
        public string AccountCode { get; init; } = null!;
        public string AccountName { get; init; } = null!;
        public StatementSection StatementSection { get; init; }
        public decimal DebitAmount { get; init; }
        public decimal CreditAmount { get; init; }
    }

    public async Task<IncomeStatementSnapshot> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        bool includeZeroLines,
        CancellationToken ct = default)
    {
        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(dimensionScopes);

        var sql = $"""
                   WITH
                   {BuildScopeCtes()}
                   activity_rows AS (
                       SELECT
                           t.account_id AS AccountId,
                           SUM(t.debit_amount) AS DebitAmount,
                           SUM(t.credit_amount) AS CreditAmount
                       FROM accounting_turnovers t
                       JOIN accounting_accounts a
                         ON a.account_id = t.account_id
                        AND a.is_deleted = FALSE
                       WHERE t.period >= @FromInclusive::date
                         AND t.period <= @ToInclusive::date
                         AND a.statement_section = ANY(@ProfitAndLossSections::smallint[])
                       {BuildScopeSetPredicate("t")}
                       GROUP BY t.account_id
                   ),
                   candidate_accounts AS (
                       SELECT
                           a.account_id AS AccountId,
                           a.code AS AccountCode,
                           a.name AS AccountName,
                           a.statement_section AS StatementSection
                       FROM accounting_accounts a
                       WHERE a.is_deleted = FALSE
                         AND a.statement_section = ANY(@ProfitAndLossSections::smallint[])
                         AND (
                             (@IncludeZeroLines::boolean = TRUE AND a.is_active = TRUE)
                             OR a.account_id IN (
                                 SELECT ar.AccountId
                                 FROM activity_rows ar
                             )
                         )
                   )
                   SELECT
                       c.AccountId,
                       c.AccountCode,
                       c.AccountName,
                       c.StatementSection,
                       COALESCE(ar.DebitAmount, 0::numeric) AS DebitAmount,
                       COALESCE(ar.CreditAmount, 0::numeric) AS CreditAmount
                   FROM candidate_accounts c
                   LEFT JOIN activity_rows ar ON ar.AccountId = c.AccountId
                   ORDER BY c.AccountCode;
                   """;

        await uow.EnsureConnectionOpenAsync(ct);

        var rows = (await uow.Connection.QueryAsync<Row>(
            new CommandDefinition(
                sql,
                new
                {
                    FromInclusive = fromInclusive,
                    ToInclusive = toInclusive,
                    IncludeZeroLines = includeZeroLines,
                    ProfitAndLossSections,
                    ScopeDimensionCount = scopeDimensionCount,
                    ScopeDimIds = scopeDimIds,
                    ScopeValueIds = scopeValueIds
                },
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        return new IncomeStatementSnapshot(
            rows.Select(x => new IncomeStatementSnapshotRow(
                    x.AccountId,
                    x.AccountCode,
                    x.AccountName,
                    x.StatementSection,
                    x.DebitAmount,
                    x.CreditAmount))
                .ToList());
    }

    private static string BuildScopeCtes()
        => """
           requested_scope_pairs AS (
               SELECT *
               FROM unnest(@ScopeDimIds::uuid[], @ScopeValueIds::uuid[]) AS sp(dimension_id, value_id)
           ),
           matching_dimension_sets AS (
               SELECT di.dimension_set_id
               FROM platform_dimension_set_items di
               JOIN requested_scope_pairs sp
                 ON sp.dimension_id = di.dimension_id
                AND sp.value_id = di.value_id
               GROUP BY di.dimension_set_id
               HAVING COUNT(DISTINCT di.dimension_id) = @ScopeDimensionCount::int
           ),
           """;

    private static string BuildScopeSetPredicate(string alias)
        => $"""
                   AND (
                       @ScopeDimensionCount::int = 0
                       OR {alias}.dimension_set_id IN (
                           SELECT dimension_set_id
                           FROM matching_dimension_sets
                       )
                   )
                   """;
}
