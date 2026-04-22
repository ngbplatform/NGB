using Dapper;
using System.Text;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// Aggregated ledger reader based on accounting_register_main:
/// groups by document + counter-account + DimensionSetId (canonical dimensions).
/// </summary>
public sealed class PostgresGeneralLedgerAggregatedReader(
    IUnitOfWork uow,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IGeneralLedgerAggregatedPageReader
{
    public async Task<GeneralLedgerAggregatedPage> GetPageAsync(
        GeneralLedgerAggregatedPageRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.AccountId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.AccountId));

        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        await uow.EnsureConnectionOpenAsync(ct);

        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(request.DimensionScopes);
        var pagingEnabled = !request.DisablePaging;
        var take = pagingEnabled ? request.PageSize + 1 : 0;
        var cursor = pagingEnabled ? request.Cursor : null;
        var sql = BuildPageSql(
            hasDimensionScopes: scopeDimensionCount > 0,
            hasCursor: cursor is not null,
            hasPaging: pagingEnabled);

        var materialized = (await uow.Connection.QueryAsync<GeneralLedgerAggregatedLine>(
            new CommandDefinition(
                sql,
                new
                {
                    AccountId = request.AccountId,
                    FromUtc = ToMonthStartUtc(request.FromInclusive),
                    ToExclusiveUtc = ToMonthStartUtc(request.ToInclusive.AddMonths(1)),
                    ScopeDimensionCount = scopeDimensionCount,
                    ScopeDimIds = scopeDimIds,
                    ScopeValueIds = scopeValueIds,
                    AfterPeriodUtc = cursor?.AfterPeriodUtc,
                    AfterDocumentId = cursor?.AfterDocumentId,
                    AfterCounterAccountCode = cursor?.AfterCounterAccountCode,
                    AfterCounterAccountId = cursor?.AfterCounterAccountId,
                    AfterDimensionSetId = cursor?.AfterDimensionSetId,
                    Take = take
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var hasMore = pagingEnabled && materialized.Count > request.PageSize;
        var lines = hasMore ? materialized.Take(request.PageSize).ToList() : materialized;

        await ResolveDimensionsAsync(lines, ct);
        await ResolveDimensionValueDisplaysAsync(lines, ct);

        GeneralLedgerAggregatedLineCursor? nextCursor = null;
        if (hasMore && lines.Count > 0)
        {
            var last = lines[^1];
            nextCursor = new GeneralLedgerAggregatedLineCursor
            {
                AfterPeriodUtc = last.PeriodUtc,
                AfterDocumentId = last.DocumentId,
                AfterCounterAccountCode = last.CounterAccountCode,
                AfterCounterAccountId = last.CounterAccountId,
                AfterDimensionSetId = last.DimensionSetId
            };
        }

        return new GeneralLedgerAggregatedPage(lines, hasMore, nextCursor);
    }

    private static string BuildPageSql(bool hasDimensionScopes, bool hasCursor, bool hasPaging)
    {
        var sql = new StringBuilder();

        sql.AppendLine("WITH");

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           requested_scope_pairs AS (
                               SELECT *
                               FROM unnest(CAST(@ScopeDimIds AS uuid[]), CAST(@ScopeValueIds AS uuid[])) AS sp(dimension_id, value_id)
                           ),
                           matching_dimension_sets AS (
                               SELECT di.dimension_set_id
                               FROM platform_dimension_set_items di
                               JOIN requested_scope_pairs sp
                                 ON sp.dimension_id = di.dimension_id
                                AND sp.value_id = di.value_id
                               GROUP BY di.dimension_set_id
                               HAVING COUNT(DISTINCT di.dimension_id) = @ScopeDimensionCount
                           ),
                           """);
        }

        sql.AppendLine("""
                       me AS (
                           SELECT account_id, code
                           FROM accounting_accounts
                           WHERE account_id = CAST(@AccountId AS uuid) AND is_deleted = FALSE
                       ),
                       agg AS (
                           SELECT
                               r.document_id AS "DocumentId",
                               MIN(r.period) AS "PeriodUtc",
                               me.account_id AS "AccountId",
                               me.code AS "AccountCode",
                               ac.account_id AS "CounterAccountId",
                               ac.code AS "CounterAccountCode",
                               r.debit_dimension_set_id AS "DimensionSetId",
                               SUM(r.amount) AS "DebitAmount",
                               0::numeric AS "CreditAmount"
                           FROM accounting_register_main r
                           CROSS JOIN me
                           JOIN accounting_accounts ac ON ac.account_id = r.credit_account_id AND ac.is_deleted = FALSE
                       """);

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           JOIN matching_dimension_sets debit_scope
                             ON debit_scope.dimension_set_id = r.debit_dimension_set_id
                           """);
        }

        sql.AppendLine("""
                           WHERE
                               r.debit_account_id = me.account_id
                               AND r.period >= CAST(@FromUtc AS timestamptz)
                               AND r.period < CAST(@ToExclusiveUtc AS timestamptz)
                       """);

        if (hasCursor)
        {
            sql.AppendLine("""
                           AND (r.period, r.document_id, ac.code, ac.account_id, r.debit_dimension_set_id) > (
                               CAST(@AfterPeriodUtc AS timestamptz),
                               CAST(@AfterDocumentId AS uuid),
                               @AfterCounterAccountCode,
                               CAST(@AfterCounterAccountId AS uuid),
                               CAST(@AfterDimensionSetId AS uuid)
                           )
                           """);
        }

        sql.AppendLine("""
                           GROUP BY
                               r.document_id,
                               me.account_id,
                               me.code,
                               ac.account_id,
                               ac.code,
                               r.debit_dimension_set_id

                           UNION ALL

                           SELECT
                               r.document_id AS "DocumentId",
                               MIN(r.period) AS "PeriodUtc",
                               me.account_id AS "AccountId",
                               me.code AS "AccountCode",
                               ad.account_id AS "CounterAccountId",
                               ad.code AS "CounterAccountCode",
                               r.credit_dimension_set_id AS "DimensionSetId",
                               0::numeric AS "DebitAmount",
                               SUM(r.amount) AS "CreditAmount"
                           FROM accounting_register_main r
                           CROSS JOIN me
                           JOIN accounting_accounts ad ON ad.account_id = r.debit_account_id AND ad.is_deleted = FALSE
                       """);

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           JOIN matching_dimension_sets credit_scope
                             ON credit_scope.dimension_set_id = r.credit_dimension_set_id
                           """);
        }

        sql.AppendLine("""
                           WHERE
                               r.credit_account_id = me.account_id
                               AND r.period >= CAST(@FromUtc AS timestamptz)
                               AND r.period < CAST(@ToExclusiveUtc AS timestamptz)
                       """);

        if (hasCursor)
        {
            sql.AppendLine("""
                           AND (r.period, r.document_id, ad.code, ad.account_id, r.credit_dimension_set_id) > (
                               CAST(@AfterPeriodUtc AS timestamptz),
                               CAST(@AfterDocumentId AS uuid),
                               @AfterCounterAccountCode,
                               CAST(@AfterCounterAccountId AS uuid),
                               CAST(@AfterDimensionSetId AS uuid)
                           )
                           """);
        }

        sql.AppendLine("""
                           GROUP BY
                               r.document_id,
                               me.account_id,
                               me.code,
                               ad.account_id,
                               ad.code,
                               r.credit_dimension_set_id
                       ),
                       final_rows AS (
                           SELECT
                               MIN("PeriodUtc") AS "PeriodUtc",
                               "DocumentId",
                               "AccountId",
                               "AccountCode",
                               "CounterAccountId",
                               "CounterAccountCode",
                               "DimensionSetId",
                               SUM("DebitAmount") AS "DebitAmount",
                               SUM("CreditAmount") AS "CreditAmount"
                           FROM agg
                           GROUP BY
                               "DocumentId",
                               "AccountId",
                               "AccountCode",
                               "CounterAccountId",
                               "CounterAccountCode",
                               "DimensionSetId"
                       )
                       SELECT
                           "PeriodUtc",
                           "DocumentId",
                           "AccountId",
                           "AccountCode",
                           "CounterAccountId",
                           "CounterAccountCode",
                           "DimensionSetId",
                           "DebitAmount",
                           "CreditAmount"
                       FROM final_rows
                       ORDER BY
                           "PeriodUtc", "DocumentId", "CounterAccountCode", "CounterAccountId", "DimensionSetId"
                       """);

        if (hasPaging)
            sql.AppendLine("LIMIT @Take;");

        return sql.ToString();
    }

    private async Task ResolveDimensionsAsync(IReadOnlyList<GeneralLedgerAggregatedLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var ids = lines
            .Select(x => x.DimensionSetId)
            .Distinct()
            .ToArray();

        var bags = await dimensionSetReader.GetBagsByIdsAsync(ids, ct);

        foreach (var l in lines)
        {
            l.Dimensions = bags.TryGetValue(l.DimensionSetId, out var bag)
                ? bag
                : DimensionBag.Empty;
        }
    }

    private async Task ResolveDimensionValueDisplaysAsync(
        IReadOnlyList<GeneralLedgerAggregatedLine> lines,
        CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var keys = lines.Select(x => x.Dimensions).CollectValueKeys();
        if (keys.Count == 0)
            return;

        var resolved = await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);

        foreach (var l in lines)
        {
            l.DimensionValueDisplays = l.Dimensions.ToValueDisplayMap(resolved);
        }
    }

    private static DateTime ToMonthStartUtc(DateOnly period)
        => new(period.Year, period.Month, period.Day, 0, 0, 0, DateTimeKind.Utc);
}
