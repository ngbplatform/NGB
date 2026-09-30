using Dapper;
using NGB.Accounting.Reports.AccountCard;

namespace NGB.PostgreSql.Readers;

public sealed partial class PostgresAccountCardEffectivePageReader
{
    private sealed class CandidateRow
    {
        public long EntryId { get; init; }
        public DateTime PeriodUtc { get; init; }
        public Guid DocumentId { get; init; }
        public Guid CounterAccountId { get; init; }
        public Guid CounterAccountDimensionSetId { get; init; }
        public Guid DimensionSetId { get; init; }
        public short Sign { get; init; }
        public decimal Amount { get; init; }
        public string AccountCode { get; init; } = "";
        public string CounterAccountCode { get; init; } = "";
        public bool IsEffective { get; init; }
    }

    private async Task<AccountCardLinePage> QueryBoundedPageAsync(
        AccountCardLinePageRequest request,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var lines = new List<AccountCardLine>(request.PageSize + 1);
        var after = request.Cursor;

        var args = new DynamicParameters(new
        {
            request.AccountId,
            FromUtc = ToMonthStartUtc(request.FromInclusive),
            ToExclusiveUtc = ToMonthStartUtc(request.ToInclusive.AddMonths(1)),
            ScopeDimIds = scopeDimIds, ScopeValueIds = scopeValueIds, ScopeDimensionCount = scopeDimensionCount,
            ScanSize = 256
        });

        // A batch may consist entirely of cancelled entries. Continue by its raw key, never by OFFSET.
        var batches = 0;
        while (lines.Count <= request.PageSize)
        {
            ct.ThrowIfCancellationRequested();
            // Dense cancellation ranges must not turn one page into hundreds of network round trips.
            if (batches++ == 4)
            {
                var full = await QueryPageWithTotalsAsync(request, scopeDimIds, scopeValueIds, scopeDimensionCount, ct);

                return new AccountCardLinePage
                {
                    Lines = full.Lines, HasMore = full.HasMore, NextCursor = full.NextCursor,
                    PrefixDelta = request.IncludePrefixDelta ? full.PrefixDelta : 0
                };
            }

            args.Add("AfterPeriodUtc", after?.AfterPeriodUtc);
            args.Add("AfterEntryId", after?.AfterEntryId);
            var batch = (await uow.Connection.QueryAsync<CandidateRow>(new CommandDefinition(
                    BuildBrowsingSql(scopeDimensionCount > 0, after is not null),
                    args,
                    uow.Transaction,
                    cancellationToken: ct)))
                .AsList();

            foreach (var row in batch.Where(r => r.IsEffective))
            {
                lines.Add(new AccountCardLine
                {
                    AccountId = request.AccountId,
                    AccountCode = row.AccountCode,
                    EntryId = row.EntryId,
                    PeriodUtc = row.PeriodUtc,
                    DocumentId = row.DocumentId,
                    CounterAccountId = row.CounterAccountId,
                    CounterAccountCode = row.CounterAccountCode,
                    DimensionSetId = row.DimensionSetId,
                    CounterAccountDimensionSetId = row.CounterAccountDimensionSetId,
                    DebitAmount = row.Sign == 1 ? row.Amount : 0,
                    CreditAmount = row.Sign == -1 ? row.Amount : 0
                });

                if (lines.Count > request.PageSize)
                    break;
            }

            if (batch.Count < 256 || lines.Count > request.PageSize)
                break;

            var last = batch[^1];
            after = new()
            {
                AfterPeriodUtc = last.PeriodUtc,
                AfterEntryId = last.EntryId
            };
        }

        var hasMore = lines.Count > request.PageSize;
        if (hasMore)
            lines.RemoveAt(lines.Count - 1);

        var prefix = 0m;
        if (request.IncludePrefixDelta && request.Cursor is { } cursor)
        {
            args.Add("AfterPeriodUtc", cursor.AfterPeriodUtc);
            args.Add("AfterEntryId", cursor.AfterEntryId);

            prefix = await uow.Connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                BuildBrowsingPrefixSql(scopeDimensionCount > 0),
                args,
                uow.Transaction,
                cancellationToken: ct));
        }

        await ResolveDimensionsAsync(lines, ct);
        await ResolveDimensionValueDisplaysAsync(lines, ct);

        var tail = lines.LastOrDefault();

        return new AccountCardLinePage
        {
            Lines = lines,
            HasMore = hasMore,
            PrefixDelta = prefix,
            NextCursor = hasMore && tail is not null
                ? new()
                {
                    AfterPeriodUtc = tail.PeriodUtc,
                    AfterEntryId = tail.EntryId
                }
                : null
        };
    }

    private static string BrowsingScopes(bool scoped) => scoped ? """
        requested AS (SELECT * FROM unnest(@ScopeDimIds::uuid[], @ScopeValueIds::uuid[]) AS p(dimension_id,value_id)),
        matching AS (
            SELECT i.dimension_set_id FROM platform_dimension_set_items i JOIN requested p USING(dimension_id,value_id)
            GROUP BY i.dimension_set_id HAVING COUNT(DISTINCT i.dimension_id)=@ScopeDimensionCount
        ),
        """ : "";

    private static string BrowsingSide(bool debit, bool scoped, bool seek, bool bounded)
    {
        var own = debit ? "debit" : "credit";
        var counter = debit ? "credit" : "debit";

        return $"""
            SELECT r.entry_id AS "EntryId", r.period AS "PeriodUtc", r.document_id AS "DocumentId",
                   r.{counter}_account_id AS "CounterAccountId", r.{counter}_dimension_set_id AS "CounterAccountDimensionSetId",
                   r.{own}_dimension_set_id AS "DimensionSetId", {(debit ? "1" : "-1")}::smallint AS "Sign", r.amount AS "Amount"
            FROM accounting_register_main r
            WHERE r.{own}_account_id=@AccountId::uuid AND r.period>=@FromUtc::timestamptz AND r.period<@ToExclusiveUtc::timestamptz
            {(scoped ? $"AND r.{own}_dimension_set_id IN (SELECT dimension_set_id FROM matching)" : "")}
            {(seek ? $"AND (r.period,r.entry_id) > (@AfterPeriodUtc::timestamptz,@AfterEntryId::bigint)" : "")}
            {(bounded ? "ORDER BY r.period,r.entry_id LIMIT @ScanSize" : "")}
            """;
    }

    private static string BuildBrowsingSql(bool scoped, bool seek) => $"""
        WITH {BrowsingScopes(scoped)}
        candidates AS MATERIALIZED (
            SELECT * FROM (({BrowsingSide(true, scoped, seek, true)}) UNION ALL ({BrowsingSide(false, scoped, seek, true)})) both_sides
            ORDER BY "PeriodUtc","EntryId" LIMIT @ScanSize
        )
        SELECT b.*, me.code AS "AccountCode", COALESCE(counter.code,'') AS "CounterAccountCode",
            (counter.account_id IS NOT NULL AND counts.rank <= CASE b."Sign" WHEN 1 THEN counts.positive-counts.negative ELSE counts.negative-counts.positive END) AS "IsEffective"
        FROM candidates b
        JOIN accounting_accounts me ON me.account_id=@AccountId::uuid AND me.is_deleted=FALSE
        LEFT JOIN accounting_accounts counter ON counter.account_id=b."CounterAccountId" AND counter.is_deleted=FALSE
        CROSS JOIN LATERAL (
            SELECT COUNT(*) FILTER (WHERE p.sign=1) AS positive, COUNT(*) FILTER (WHERE p.sign=-1) AS negative,
                   COUNT(*) FILTER (WHERE p.sign=b."Sign" AND (p.period,p.entry_id)<=(b."PeriodUtc",b."EntryId")) AS rank
            FROM (
                SELECT r.period,r.entry_id,1 AS sign FROM accounting_register_main r
                WHERE r.document_id=b."DocumentId" AND r.debit_account_id=@AccountId::uuid
                  AND r.credit_account_id=b."CounterAccountId" AND r.debit_dimension_set_id=b."DimensionSetId" AND r.amount=b."Amount"
                  AND r.period>=@FromUtc::timestamptz AND r.period<@ToExclusiveUtc::timestamptz
                UNION ALL
                SELECT r.period,r.entry_id,-1 FROM accounting_register_main r
                WHERE r.document_id=b."DocumentId" AND r.credit_account_id=@AccountId::uuid
                  AND r.debit_account_id=b."CounterAccountId" AND r.credit_dimension_set_id=b."DimensionSetId" AND r.amount=b."Amount"
                  AND r.period>=@FromUtc::timestamptz AND r.period<@ToExclusiveUtc::timestamptz
            ) p
        ) counts
        ORDER BY b."PeriodUtc",b."EntryId";
        """;

    private static string BuildBrowsingPrefixSql(bool scoped) => $"""
        WITH {BrowsingScopes(scoped)}
        lines AS NOT MATERIALIZED (({BrowsingSide(true, scoped, false, false)}) UNION ALL ({BrowsingSide(false, scoped, false, false)})),
        prefix_documents AS (
            SELECT DISTINCT "DocumentId" FROM lines
            WHERE ("PeriodUtc","EntryId") <= (@AfterPeriodUtc::timestamptz,@AfterEntryId::bigint)
        ),
        counts AS (
            SELECT b."Amount" AS amount,
                   COUNT(*) FILTER(WHERE b."Sign"=1) AS positive,
                   COUNT(*) FILTER(WHERE b."Sign"=-1) AS negative,
                   COUNT(*) FILTER(WHERE b."Sign"=1 AND (b."PeriodUtc",b."EntryId") <= (@AfterPeriodUtc::timestamptz,@AfterEntryId::bigint)) AS positive_before,
                   COUNT(*) FILTER(WHERE b."Sign"=-1 AND (b."PeriodUtc",b."EntryId") <= (@AfterPeriodUtc::timestamptz,@AfterEntryId::bigint)) AS negative_before
            FROM lines b JOIN prefix_documents d USING("DocumentId")
            JOIN accounting_accounts a ON a.account_id=b."CounterAccountId" AND a.is_deleted=FALSE
            GROUP BY b."DocumentId",b."CounterAccountId",b."DimensionSetId",b."Amount"
        )
        SELECT COALESCE(SUM(amount * (LEAST(positive_before,GREATEST(positive-negative,0))-LEAST(negative_before,GREATEST(negative-positive,0)))),0)
        FROM counts;
        """;
}
