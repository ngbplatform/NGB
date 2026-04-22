using Dapper;
using System.Text;
using NGB.Accounting.Reports.AccountCard;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountCardEffectivePageReader(
    IUnitOfWork uow,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IAccountCardEffectivePageReader
{
    private sealed class PageWithTotalsRow
    {
        public long? EntryId { get; init; }
        public DateTime? PeriodUtc { get; init; }
        public Guid? DocumentId { get; init; }
        public Guid? AccountId { get; init; }
        public string? AccountCode { get; init; }
        public Guid? CounterAccountId { get; init; }
        public string? CounterAccountCode { get; init; }
        public Guid? CounterAccountDimensionSetId { get; init; }
        public Guid? DimensionSetId { get; init; }
        public decimal? DebitAmount { get; init; }
        public decimal? CreditAmount { get; init; }
        public decimal TotalDebit { get; init; }
        public decimal TotalCredit { get; init; }
        public bool HasMore { get; init; }
    }

    public async Task<AccountCardLinePage> GetPageAsync(
        AccountCardLinePageRequest request,
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
        return request.IncludeTotals
            ? await QueryPageWithTotalsAsync(request, scopeDimIds, scopeValueIds, scopeDimensionCount, ct)
            : await QueryPageOnlyAsync(request, scopeDimIds, scopeValueIds, scopeDimensionCount, ct);
    }

    private async Task<AccountCardLinePage> QueryPageOnlyAsync(
        AccountCardLinePageRequest request,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var pagingEnabled = !request.DisablePaging;
        var sql = BuildPageSql(
            hasDimensionScopes: scopeDimensionCount > 0,
            hasCursor: pagingEnabled && request.Cursor is not null,
            includeTotals: false,
            disablePaging: request.DisablePaging);

        var rows = (await uow.Connection.QueryAsync<AccountCardLine>(
            new CommandDefinition(
                sql,
                new
                {
                    request.AccountId,
                    FromUtc = ToMonthStartUtc(request.FromInclusive),
                    ToExclusiveUtc = ToMonthStartUtc(request.ToInclusive.AddMonths(1)),
                    ScopeDimensionCount = scopeDimensionCount,
                    ScopeDimIds = scopeDimIds,
                    ScopeValueIds = scopeValueIds,
                    AfterPeriodUtc = pagingEnabled ? request.Cursor?.AfterPeriodUtc : null,
                    AfterEntryId = pagingEnabled ? request.Cursor?.AfterEntryId : null,
                    LimitPlusOne = request.PageSize + 1
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var hasMore = pagingEnabled && rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        await ResolveDimensionsAsync(rows, ct);
        await ResolveDimensionValueDisplaysAsync(rows, ct);

        return new AccountCardLinePage
        {
            Lines = rows,
            HasMore = hasMore,
            NextCursor = BuildNextCursor(rows, hasMore)
        };
    }

    private async Task<AccountCardLinePage> QueryPageWithTotalsAsync(
        AccountCardLinePageRequest request,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var pagingEnabled = !request.DisablePaging;
        var sql = BuildPageSql(
            hasDimensionScopes: scopeDimensionCount > 0,
            hasCursor: pagingEnabled && request.Cursor is not null,
            includeTotals: true,
            disablePaging: request.DisablePaging);

        var rows = (await uow.Connection.QueryAsync<PageWithTotalsRow>(
            new CommandDefinition(
                sql,
                new
                {
                    request.AccountId,
                    FromUtc = ToMonthStartUtc(request.FromInclusive),
                    ToExclusiveUtc = ToMonthStartUtc(request.ToInclusive.AddMonths(1)),
                    ScopeDimensionCount = scopeDimensionCount,
                    ScopeDimIds = scopeDimIds,
                    ScopeValueIds = scopeValueIds,
                    AfterPeriodUtc = pagingEnabled ? request.Cursor?.AfterPeriodUtc : null,
                    AfterEntryId = pagingEnabled ? request.Cursor?.AfterEntryId : null,
                    PageSize = request.PageSize,
                    LimitPlusOne = request.PageSize + 1
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var totalDebit = rows.Count == 0 ? 0m : rows[0].TotalDebit;
        var totalCredit = rows.Count == 0 ? 0m : rows[0].TotalCredit;
        var hasMore = rows.Count > 0 && rows[0].HasMore;

        var lines = rows
            .Where(x => x.EntryId.HasValue)
            .Select(ToLine)
            .ToList();

        await ResolveDimensionsAsync(lines, ct);
        await ResolveDimensionValueDisplaysAsync(lines, ct);

        return new AccountCardLinePage
        {
            Lines = lines,
            HasMore = hasMore,
            NextCursor = BuildNextCursor(lines, hasMore),
            TotalDebit = totalDebit,
            TotalCredit = totalCredit
        };
    }

    private static AccountCardLine ToLine(PageWithTotalsRow row)
        => new()
        {
            EntryId = row.EntryId!.Value,
            PeriodUtc = row.PeriodUtc!.Value,
            DocumentId = row.DocumentId!.Value,
            AccountId = row.AccountId!.Value,
            AccountCode = row.AccountCode!,
            CounterAccountId = row.CounterAccountId!.Value,
            CounterAccountCode = row.CounterAccountCode!,
            CounterAccountDimensionSetId = row.CounterAccountDimensionSetId!.Value,
            DimensionSetId = row.DimensionSetId!.Value,
            DebitAmount = row.DebitAmount!.Value,
            CreditAmount = row.CreditAmount!.Value
        };

    private static AccountCardLineCursor? BuildNextCursor(IReadOnlyList<AccountCardLine> lines, bool hasMore)
    {
        if (!hasMore || lines.Count == 0)
            return null;

        var last = lines[^1];
        return new AccountCardLineCursor
        {
            AfterPeriodUtc = last.PeriodUtc,
            AfterEntryId = last.EntryId
        };
    }

    private async Task ResolveDimensionsAsync(IReadOnlyList<AccountCardLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var ids = lines
            .SelectMany(x => new[] { x.DimensionSetId, x.CounterAccountDimensionSetId })
            .Distinct()
            .ToArray();

        var bags = await dimensionSetReader.GetBagsByIdsAsync(ids, ct);

        foreach (var l in lines)
        {
            l.Dimensions = bags.TryGetValue(l.DimensionSetId, out var primary)
                ? primary
                : DimensionBag.Empty;

            l.CounterAccountDimensions = bags.TryGetValue(l.CounterAccountDimensionSetId, out var counter)
                ? counter
                : DimensionBag.Empty;
        }
    }

    private async Task ResolveDimensionValueDisplaysAsync(IReadOnlyList<AccountCardLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var keys = lines
            .SelectMany(x => new[] { x.Dimensions, x.CounterAccountDimensions })
            .CollectValueKeys();

        if (keys.Count == 0)
            return;

        var resolved = await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);

        foreach (var l in lines)
        {
            l.DimensionValueDisplays = l.Dimensions.ToValueDisplayMap(resolved);
            l.CounterAccountDimensionValueDisplays = l.CounterAccountDimensions.ToValueDisplayMap(resolved);
        }
    }

    private static string BuildPageSql(bool hasDimensionScopes, bool hasCursor, bool includeTotals, bool disablePaging)
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
                       base_lines AS (
                           SELECT
                               r.entry_id AS "EntryId",
                               r.period AS "PeriodUtc",
                               r.document_id AS "DocumentId",
                               me.account_id AS "AccountId",
                               me.code AS "AccountCode",
                               ac.account_id AS "CounterAccountId",
                               ac.code AS "CounterAccountCode",
                               r.credit_dimension_set_id AS "CounterAccountDimensionSetId",
                               r.debit_dimension_set_id AS "DimensionSetId",
                               1::smallint AS "Sign",
                               r.amount AS "Amount"
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

                           UNION ALL

                           SELECT
                               r.entry_id AS "EntryId",
                               r.period AS "PeriodUtc",
                               r.document_id AS "DocumentId",
                               me.account_id AS "AccountId",
                               me.code AS "AccountCode",
                               ad.account_id AS "CounterAccountId",
                               ad.code AS "CounterAccountCode",
                               r.debit_dimension_set_id AS "CounterAccountDimensionSetId",
                               r.credit_dimension_set_id AS "DimensionSetId",
                               (-1)::smallint AS "Sign",
                               r.amount AS "Amount"
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
                       ),
                       ranked AS (
                           SELECT
                               b.*,
                               row_number() OVER (
                                   PARTITION BY b."DocumentId", b."CounterAccountId", b."DimensionSetId", b."Amount", b."Sign"
                                   ORDER BY b."PeriodUtc", b."EntryId"
                               ) AS "SignRank",
                               count(*) FILTER (WHERE b."Sign" = 1) OVER (
                                   PARTITION BY b."DocumentId", b."CounterAccountId", b."DimensionSetId", b."Amount"
                               ) AS "PositiveCount",
                               count(*) FILTER (WHERE b."Sign" = -1) OVER (
                                   PARTITION BY b."DocumentId", b."CounterAccountId", b."DimensionSetId", b."Amount"
                               ) AS "NegativeCount"
                           FROM base_lines b
                       ),
                       effective_lines AS
                       """);

        if (includeTotals)
            sql.Append("MATERIALIZED ");

        sql.AppendLine("""
                       (
                           SELECT
                               "EntryId",
                               "PeriodUtc",
                               "DocumentId",
                               "AccountId",
                               "AccountCode",
                               "CounterAccountId",
                               "CounterAccountCode",
                               "CounterAccountDimensionSetId",
                               "DimensionSetId",
                               CASE WHEN "Sign" = 1 THEN "Amount" ELSE 0::numeric END AS "DebitAmount",
                               CASE WHEN "Sign" = -1 THEN "Amount" ELSE 0::numeric END AS "CreditAmount"
                           FROM ranked
                           WHERE
                               ("PositiveCount" > "NegativeCount" AND "Sign" = 1 AND "SignRank" <= ("PositiveCount" - "NegativeCount"))
                               OR
                               ("NegativeCount" > "PositiveCount" AND "Sign" = -1 AND "SignRank" <= ("NegativeCount" - "PositiveCount"))
                       )
                       """);

        if (!includeTotals)
        {
            sql.AppendLine("""
                           SELECT *
                           FROM effective_lines
                           WHERE 1 = 1
                           """);

            if (hasCursor)
                sql.AppendLine("""  AND ("PeriodUtc", "EntryId") > (CAST(@AfterPeriodUtc AS timestamptz), @AfterEntryId)""");

            sql.AppendLine("ORDER BY \"PeriodUtc\", \"EntryId\"");
            if (disablePaging)
            {
                sql.AppendLine(";");
            }
            else
            {
                sql.AppendLine("LIMIT @LimitPlusOne;");
            }

            return sql.ToString();
        }

        if (disablePaging)
        {
            sql.AppendLine("""
                       ,
                       totals AS (
                           SELECT
                               COALESCE(SUM("DebitAmount"), 0) AS "TotalDebit",
                               COALESCE(SUM("CreditAmount"), 0) AS "TotalCredit"
                           FROM effective_lines
                       )
                       SELECT
                           p."EntryId",
                           p."PeriodUtc",
                           p."DocumentId",
                           p."AccountId",
                           p."AccountCode",
                           p."CounterAccountId",
                           p."CounterAccountCode",
                           p."CounterAccountDimensionSetId",
                           p."DimensionSetId",
                           p."DebitAmount",
                           p."CreditAmount",
                           t."TotalDebit",
                           t."TotalCredit",
                           FALSE AS "HasMore"
                       FROM totals t
                       LEFT JOIN effective_lines p ON TRUE
                       ORDER BY p."PeriodUtc" NULLS LAST, p."EntryId" NULLS LAST;
                       """);

            return sql.ToString();
        }

        sql.AppendLine("""
                       ,
                       paged_raw AS (
                           SELECT *
                           FROM effective_lines
                           WHERE 1 = 1
                       """);

        if (hasCursor)
            sql.AppendLine("""      AND ("PeriodUtc", "EntryId") > (CAST(@AfterPeriodUtc AS timestamptz), @AfterEntryId)""");

        sql.AppendLine("""
                           ORDER BY "PeriodUtc", "EntryId"
                           LIMIT @LimitPlusOne
                       ),
                       paged AS (
                           SELECT *
                           FROM paged_raw
                           ORDER BY "PeriodUtc", "EntryId"
                           LIMIT @PageSize
                       ),
                       page_flags AS (
                           SELECT COUNT(*) > @PageSize AS "HasMore"
                           FROM paged_raw
                       ),
                       totals AS (
                           SELECT
                               COALESCE(SUM("DebitAmount"), 0) AS "TotalDebit",
                               COALESCE(SUM("CreditAmount"), 0) AS "TotalCredit"
                           FROM effective_lines
                       )
                       SELECT
                           p."EntryId",
                           p."PeriodUtc",
                           p."DocumentId",
                           p."AccountId",
                           p."AccountCode",
                           p."CounterAccountId",
                           p."CounterAccountCode",
                           p."CounterAccountDimensionSetId",
                           p."DimensionSetId",
                           p."DebitAmount",
                           p."CreditAmount",
                           t."TotalDebit",
                           t."TotalCredit",
                           f."HasMore"
                       FROM totals t
                       CROSS JOIN page_flags f
                       LEFT JOIN paged p ON TRUE
                       ORDER BY p."PeriodUtc" NULLS LAST, p."EntryId" NULLS LAST;
                       """);

        return sql.ToString();
    }

    private static DateTime ToMonthStartUtc(DateOnly period)
        => new(period.Year, period.Month, period.Day, 0, 0, 0, DateTimeKind.Utc);
}
