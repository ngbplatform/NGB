using Dapper;
using NGB.Accounting.Reports.GeneralLedgerAggregated;

namespace NGB.PostgreSql.Readers;

public sealed partial class PostgresGeneralLedgerAggregatedReader
{
    private sealed record CandidateDocument(DateTime Period, Guid DocumentId);

    private async Task<List<GeneralLedgerAggregatedLine>?> TryReadBoundedAsync(
        GeneralLedgerAggregatedPageRequest request,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var result = new List<GeneralLedgerAggregatedLine>(request.PageSize + 1);
        var cursor = request.Cursor;

        var args = new DynamicParameters(new
        {
            request.AccountId, FromUtc = ToMonthStartUtc(request.FromInclusive), ToExclusiveUtc = ToMonthStartUtc(request.ToInclusive.AddMonths(1)),
            ScopeDimIds = scopeDimIds, ScopeValueIds = scopeValueIds, ScopeDimensionCount = scopeDimensionCount,
            AfterPeriodUtc = cursor?.AfterPeriodUtc, AfterDocumentId = cursor?.AfterDocumentId,
            AfterCounterAccountCode = cursor?.AfterCounterAccountCode, AfterCounterAccountId = cursor?.AfterCounterAccountId,
            AfterDimensionSetId = cursor?.AfterDimensionSetId,
            ScanAfterPeriod = cursor?.AfterPeriodUtc, ScanAfterDocument = cursor?.AfterDocumentId
        });

        // Read ordered raw keys first, then aggregate complete documents. A document can have
        // postings on multiple dates, so applying the UI cursor to raw postings would split groups.
        // Bound network round trips even when many raw candidates produce no visible groups.
        for (var batch = 0; batch < 4; batch++)
        {
            var seek = batch > 0 || cursor is not null;
            var comparator = batch > 0 ? ">" : ">=";
            string Side(string side) => $"""
                SELECT period AS "Period", document_id AS "DocumentId" FROM accounting_register_main
                WHERE {side}_account_id=@AccountId::uuid AND period>=@FromUtc::timestamptz AND period<@ToExclusiveUtc::timestamptz
                {(seek ? $"AND (period,document_id) {comparator} (@ScanAfterPeriod::timestamptz,@ScanAfterDocument::uuid)" : "")}
                ORDER BY period,document_id LIMIT 256
                """;
            var candidates = (await uow.Connection.QueryAsync<CandidateDocument>(new CommandDefinition($"""
                SELECT * FROM (({Side("debit")}) UNION ALL ({Side("credit")})) candidates
                ORDER BY "Period","DocumentId" LIMIT 256;
                """, args, uow.Transaction, cancellationToken: ct))).AsList();

            if (candidates.Count == 0)
                return result;

            var last = candidates[^1];
            args.Add("CandidateDocuments", candidates.Select(c => c.DocumentId).Distinct().ToArray());
            args.Add("ScanToPeriod", last.Period);
            args.Add("ScanToDocument", last.DocumentId);
            args.Add("Take", request.PageSize + 1 - result.Count);

            result.AddRange(await uow.Connection.QueryAsync<GeneralLedgerAggregatedLine>(new CommandDefinition(
                BuildSql(
                    scopeDimensionCount > 0,
                    cursor is not null,
                    true,
                    false,
                    boundedDocuments: true,
                    afterScan: batch > 0),
                args, uow.Transaction,
                cancellationToken: ct)));

            if (result.Count > request.PageSize || candidates.Count < 256)
                return result;

            args.Add("ScanAfterPeriod", last.Period);
            args.Add("ScanAfterDocument", last.DocumentId);
        }

        // Fall back to one set-based range query, never an unbounded loop of small SQL requests.
        return null;
    }
}
