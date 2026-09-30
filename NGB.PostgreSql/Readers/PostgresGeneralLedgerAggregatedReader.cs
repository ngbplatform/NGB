using System.Runtime.CompilerServices;
using Dapper;
using NGB.Accounting.Reports;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// Aggregated ledger reader based on accounting_register_main:
/// groups by document + counter-account + DimensionSetId (canonical dimensions).
/// </summary>
public sealed partial class PostgresGeneralLedgerAggregatedReader(
    IUnitOfWork uow,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IGeneralLedgerAggregatedPageReader, IGeneralLedgerAggregatedStreamReader
{
    public async IAsyncEnumerable<IReadOnlyList<GeneralLedgerAggregatedLine>> ReadAsync(
        AccountActivityQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        query.EnsureInvariant();
        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(query.DimensionScopes);
        var sql = BuildPageSql(scopeDimensionCount > 0, hasCursor: false, hasPaging: false);
        var args = new
        {
            query.AccountId,
            FromUtc = ToMonthStartUtc(query.FromInclusive),
            ToExclusiveUtc = ToMonthStartUtc(query.ToInclusive.AddMonths(1)),
            ScopeDimensionCount = scopeDimensionCount,
            ScopeDimIds = scopeDimIds,
            ScopeValueIds = scopeValueIds
        };

        await foreach (var batch in PostgresReportCursorStream.ReadAsync<GeneralLedgerAggregatedLine>(uow, sql, args, ct))
        {
            await ResolveDimensionsAsync(batch, ct);
            await ResolveDimensionValueDisplaysAsync(batch, ct);
            yield return batch;
        }
    }

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
        var bounded = pagingEnabled
            ? await TryReadBoundedAsync(request, scopeDimIds, scopeValueIds, scopeDimensionCount, ct)
            : null;
        var sql = BuildPageSql(
            hasDimensionScopes: scopeDimensionCount > 0,
            hasCursor: cursor is not null,
            hasPaging: pagingEnabled,
            includePrefix: request.IncludePrefixDelta && cursor is not null);

        var parameters = new
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
                    Take = bounded is not null ? 0 : take
                };
        decimal prefixDelta = 0;
        List<GeneralLedgerAggregatedLine> materialized;
        if (bounded is not null)
        {
            materialized = bounded;
            if (request.IncludePrefixDelta && cursor is not null)
            {
                prefixDelta = await uow.Connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    BuildSql(scopeDimensionCount > 0, true, false, false, prefixOnly: true),
                    parameters,
                    uow.Transaction,
                    cancellationToken: ct));
            }
        }
        else if (request.IncludePrefixDelta && cursor is not null)
        {
            materialized = (await uow.Connection.QueryAsync<GeneralLedgerAggregatedLine, PrefixRow, GeneralLedgerAggregatedLine>(
                    new CommandDefinition(sql, parameters, uow.Transaction, cancellationToken: ct),
                    (line, prefix) => { prefixDelta = prefix.PrefixDelta; return line; }, splitOn: "PrefixDelta"))
                .Where(line => line.DocumentId != Guid.Empty).ToList();
        }
        else
        {
            materialized = (await uow.Connection.QueryAsync<GeneralLedgerAggregatedLine>(
                new CommandDefinition(sql, parameters, uow.Transaction, cancellationToken: ct))).AsList();
        }

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

        return new GeneralLedgerAggregatedPage(lines, hasMore, nextCursor, prefixDelta);
    }

    private sealed class PrefixRow
    {
        public decimal PrefixDelta { get; init; }
    }

    private static string BuildPageSql(
        bool hasDimensionScopes,
        bool hasCursor,
        bool hasPaging,
        bool includePrefix = false)
        => BuildSql(hasDimensionScopes, hasCursor, hasPaging, includePrefix);

    private static string BuildSql(
        bool hasDimensionScopes,
        bool hasCursor,
        bool hasPaging,
        bool includePrefix,
        bool boundedDocuments = false,
        bool afterScan = false,
        bool prefixOnly = false)
    {
        var scopes = hasDimensionScopes ? """
            requested_scope_pairs AS (
                SELECT * FROM unnest(CAST(@ScopeDimIds AS uuid[]), CAST(@ScopeValueIds AS uuid[])) AS sp(dimension_id, value_id)
            ),
            matching_dimension_sets AS (
                SELECT di.dimension_set_id FROM platform_dimension_set_items di
                JOIN requested_scope_pairs sp ON sp.dimension_id = di.dimension_id AND sp.value_id = di.value_id
                GROUP BY di.dimension_set_id HAVING COUNT(DISTINCT di.dimension_id) = @ScopeDimensionCount
            ),
            """ : string.Empty;
        var debitScope = hasDimensionScopes ? "JOIN matching_dimension_sets ds ON ds.dimension_set_id = r.debit_dimension_set_id" : "";
        var creditScope = hasDimensionScopes ? "JOIN matching_dimension_sets ds ON ds.dimension_set_id = r.credit_dimension_set_id" : "";
        var seek = hasCursor ? $"""
            WHERE ("PeriodUtc", "DocumentId", "CounterAccountCode", "CounterAccountId", "DimensionSetId") > (
                @AfterPeriodUtc::timestamptz, @AfterDocumentId::uuid, @AfterCounterAccountCode,
                @AfterCounterAccountId::uuid, @AfterDimensionSetId::uuid)
            """ : "";
        const string order = "\"PeriodUtc\", \"DocumentId\", \"CounterAccountCode\", \"CounterAccountId\", \"DimensionSetId\"";
        var pageSql = $"SELECT * FROM final_rows {seek} ORDER BY {order} {(hasPaging ? "LIMIT @Take" : "")}";
        var tail = includePrefix ? $"""
            , prefix AS (
                SELECT COALESCE(SUM("DebitAmount"-"CreditAmount"),0) AS "PrefixDelta"
                FROM final_rows {seek.Replace(">", "<=", StringComparison.Ordinal)}
            ), page AS ({pageSql})
            SELECT page.*, prefix."PrefixDelta" FROM prefix LEFT JOIN page ON TRUE
            ORDER BY {order};
            """ : pageSql + ";";

        if (prefixOnly)
            tail = $"SELECT COALESCE(SUM(\"DebitAmount\"-\"CreditAmount\"),0) FROM final_rows {seek.Replace(">", "<=", StringComparison.Ordinal)};";

        // Only documents that have a posting at/before the cursor can contribute to its prefix.
        // Read all dates of those documents to preserve the original group minimum and full sum.
        var prefixDocuments = prefixOnly ? """
            prefix_documents AS (
                SELECT document_id FROM accounting_register_main
                WHERE debit_account_id=@AccountId::uuid AND period>=@FromUtc::timestamptz AND period<@ToExclusiveUtc::timestamptz
                  AND (period,document_id)<=(@AfterPeriodUtc::timestamptz,@AfterDocumentId::uuid)
                UNION
                SELECT document_id FROM accounting_register_main
                WHERE credit_account_id=@AccountId::uuid AND period>=@FromUtc::timestamptz AND period<@ToExclusiveUtc::timestamptz
                  AND (period,document_id)<=(@AfterPeriodUtc::timestamptz,@AfterDocumentId::uuid)
            ),
            """ : "";
       
        // Aggregate narrow posting keys once. Apply the seek to complete groups: postings of
        // one document can span dates, so filtering raw postings by the cursor splits a group.
        return $"""
            WITH {scopes} {prefixDocuments}
            activity AS (
                SELECT r.document_id, r.period, r.credit_account_id AS counter_id,
                       r.debit_dimension_set_id AS dimension_set_id, r.amount AS debit, 0::numeric AS credit
                FROM accounting_register_main r {debitScope}
                WHERE r.debit_account_id = @AccountId::uuid AND r.period >= @FromUtc::timestamptz AND r.period < @ToExclusiveUtc::timestamptz
                {(boundedDocuments ? "AND r.document_id = ANY(@CandidateDocuments::uuid[])" : "")}
                {(prefixOnly ? "AND r.document_id IN (SELECT document_id FROM prefix_documents)" : "")}
                UNION ALL
                SELECT r.document_id, r.period, r.debit_account_id AS counter_id,
                       r.credit_dimension_set_id AS dimension_set_id, 0::numeric AS debit, r.amount AS credit
                FROM accounting_register_main r {creditScope}
                WHERE r.credit_account_id = @AccountId::uuid AND r.period >= @FromUtc::timestamptz AND r.period < @ToExclusiveUtc::timestamptz
                {(boundedDocuments ? "AND r.document_id = ANY(@CandidateDocuments::uuid[])" : "")}
                {(prefixOnly ? "AND r.document_id IN (SELECT document_id FROM prefix_documents)" : "")}
            ),
            grouped AS (
                SELECT document_id, MIN(period) AS period, counter_id, dimension_set_id,
                       SUM(debit) AS debit, SUM(credit) AS credit
                FROM activity GROUP BY document_id, counter_id, dimension_set_id
            ),
            final_rows AS (
                SELECT g.period AS "PeriodUtc", g.document_id AS "DocumentId",
                       me.account_id AS "AccountId", me.code AS "AccountCode",
                       counter.account_id AS "CounterAccountId", counter.code AS "CounterAccountCode",
                       g.dimension_set_id AS "DimensionSetId", g.debit AS "DebitAmount", g.credit AS "CreditAmount"
                FROM grouped g
                JOIN accounting_accounts counter ON counter.account_id = g.counter_id AND counter.is_deleted = FALSE
                JOIN accounting_accounts me ON me.account_id = @AccountId::uuid AND me.is_deleted = FALSE
                {(boundedDocuments ? "WHERE (g.period,g.document_id) <= (@ScanToPeriod::timestamptz,@ScanToDocument::uuid)" : "")}
                {(afterScan ? "AND (g.period,g.document_id) > (@ScanAfterPeriod::timestamptz,@ScanAfterDocument::uuid)" : "")}
            )
            {tail}
            """;
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
