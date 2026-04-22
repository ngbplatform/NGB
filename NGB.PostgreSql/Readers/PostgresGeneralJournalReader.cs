using Dapper;
using System.Text;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresGeneralJournalReader(
    IUnitOfWork uow,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader enrichmentReader)
    : IGeneralJournalReader
{
    public async Task<GeneralJournalPage> GetPageAsync(GeneralJournalPageRequest request, CancellationToken ct = default)
    {
        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        await uow.EnsureConnectionOpenAsync(ct);

        var pagingEnabled = !request.DisablePaging;
        var afterPeriodUtc = pagingEnabled ? request.Cursor?.AfterPeriodUtc : null;
        var afterEntryId = pagingEnabled ? request.Cursor?.AfterEntryId ?? 0L : 0L;
        var limit = pagingEnabled ? request.PageSize + 1 : 0;
        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(request.DimensionScopes);
        var sql = BuildPageSql(
            hasDocumentFilter: request.DocumentId.HasValue,
            hasDebitAccountFilter: request.DebitAccountId.HasValue,
            hasCreditAccountFilter: request.CreditAccountId.HasValue,
            hasStornoFilter: request.IsStorno.HasValue,
            hasDimensionScopes: scopeDimensionCount > 0,
            hasCursor: afterPeriodUtc.HasValue,
            hasPaging: pagingEnabled);

        var rows = (await uow.Connection.QueryAsync<GeneralJournalLine>(
            new CommandDefinition(
                sql,
                new
                {
                    FromUtc = ToMonthStartUtc(request.FromInclusive),
                    ToExclusiveUtc = ToMonthStartUtc(request.ToInclusive.AddMonths(1)),
                    request.DocumentId,
                    request.DebitAccountId,
                    request.CreditAccountId,
                    request.IsStorno,
                    ScopeDimensionCount = scopeDimensionCount,
                    ScopeDimIds = scopeDimIds,
                    ScopeValueIds = scopeValueIds,
                    AfterPeriodUtc = afterPeriodUtc,
                    AfterEntryId = afterEntryId,
                    Limit = limit
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var hasMore = pagingEnabled && rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        await ResolveDimensionsAsync(rows, ct);
        await ResolveDimensionValueDisplaysAsync(rows, ct);

        GeneralJournalCursor? nextCursor = null;

        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            nextCursor = new GeneralJournalCursor(last.PeriodUtc, last.EntryId);
        }

        return new GeneralJournalPage(rows, hasMore, nextCursor);
    }

    private async Task ResolveDimensionsAsync(IReadOnlyList<GeneralJournalLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var ids = lines
            .SelectMany(x => new[] { x.DebitDimensionSetId, x.CreditDimensionSetId })
            .Distinct()
            .ToArray();

        var bags = await dimensionSetReader.GetBagsByIdsAsync(ids, ct);

        foreach (var l in lines)
        {
            l.DebitDimensions = bags.TryGetValue(l.DebitDimensionSetId, out var debit)
                ? debit
                : DimensionBag.Empty;

            l.CreditDimensions = bags.TryGetValue(l.CreditDimensionSetId, out var credit)
                ? credit
                : DimensionBag.Empty;
        }
    }

    private async Task ResolveDimensionValueDisplaysAsync(IReadOnlyList<GeneralJournalLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var keys = lines
            .SelectMany(x => new[] { x.DebitDimensions, x.CreditDimensions })
            .CollectValueKeys();

        if (keys.Count == 0)
            return;

        var resolved = await enrichmentReader.ResolveAsync(keys, ct);

        foreach (var l in lines)
        {
            l.DebitDimensionValueDisplays = l.DebitDimensions.ToValueDisplayMap(resolved);
            l.CreditDimensionValueDisplays = l.CreditDimensions.ToValueDisplayMap(resolved);
        }
    }

    private static string BuildPageSql(
        bool hasDocumentFilter,
        bool hasDebitAccountFilter,
        bool hasCreditAccountFilter,
        bool hasStornoFilter,
        bool hasDimensionScopes,
        bool hasCursor,
        bool hasPaging)
    {
        var sql = new StringBuilder();

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           WITH requested_scope_pairs AS (
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
                           )
                           """);
        }

        sql.AppendLine("""
                       SELECT
                           r.entry_id      AS "EntryId",
                           r.period        AS "PeriodUtc",
                           r.document_id   AS "DocumentId",
                           r.debit_account_id   AS "DebitAccountId",
                           ad.code              AS "DebitAccountCode",
                           r.debit_dimension_set_id AS "DebitDimensionSetId",
                           r.credit_account_id  AS "CreditAccountId",
                           ac.code              AS "CreditAccountCode",
                           r.credit_dimension_set_id AS "CreditDimensionSetId",
                           r.amount        AS "Amount",
                           r.is_storno     AS "IsStorno"
                       FROM accounting_register_main r
                       JOIN accounting_accounts ad ON ad.account_id = r.debit_account_id AND ad.is_deleted = FALSE
                       JOIN accounting_accounts ac ON ac.account_id = r.credit_account_id AND ac.is_deleted = FALSE
                       """);

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           LEFT JOIN matching_dimension_sets debit_scope
                             ON debit_scope.dimension_set_id = r.debit_dimension_set_id
                           LEFT JOIN matching_dimension_sets credit_scope
                             ON credit_scope.dimension_set_id = r.credit_dimension_set_id
                           """);
        }

        sql.AppendLine("""
                       WHERE
                           r.period >= CAST(@FromUtc AS timestamptz)
                           AND r.period < CAST(@ToExclusiveUtc AS timestamptz)
                       """);

        if (hasDocumentFilter)
            sql.AppendLine("  AND r.document_id = CAST(@DocumentId AS uuid)");

        if (hasDebitAccountFilter)
            sql.AppendLine("  AND r.debit_account_id = CAST(@DebitAccountId AS uuid)");

        if (hasCreditAccountFilter)
            sql.AppendLine("  AND r.credit_account_id = CAST(@CreditAccountId AS uuid)");

        if (hasStornoFilter)
            sql.AppendLine("  AND r.is_storno = @IsStorno");

        if (hasDimensionScopes)
            sql.AppendLine("  AND (debit_scope.dimension_set_id IS NOT NULL OR credit_scope.dimension_set_id IS NOT NULL)");

        if (hasCursor)
            sql.AppendLine("  AND (r.period, r.entry_id) > (CAST(@AfterPeriodUtc AS timestamptz), @AfterEntryId)");

        sql.AppendLine("ORDER BY r.period, r.entry_id");
        if (hasPaging)
            sql.AppendLine("LIMIT @Limit;");

        return sql.ToString();
    }

    private static DateTime ToMonthStartUtc(DateOnly period)
        => new(period.Year, period.Month, period.Day, 0, 0, 0, DateTimeKind.Utc);
}
