using Dapper;
using NGB.Accounting.Documents;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents.GeneralJournalEntry;

public sealed class PostgresGeneralJournalEntryUiQueryRepository(IUnitOfWork uow)
    : IGeneralJournalEntryUiQueryRepository
{
    public async Task<GeneralJournalEntryPageRecord> GetPageAsync(
        int offset,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        CancellationToken ct = default)
        => await GetPageCoreAsync(offset, limit, search, dateFrom, dateTo, trash, null, false, ct);

    public async Task<GeneralJournalEntryPageRecord> GetCursorPageAsync(
        GeneralJournalEntryPageCursor cursor,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        CancellationToken ct = default)
        => await GetPageCoreAsync(cursor.Offset, limit, search, dateFrom, dateTo, trash, cursor, true, ct);

    private async Task<GeneralJournalEntryPageRecord> GetPageCoreAsync(
        int offset,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        GeneralJournalEntryPageCursor? cursor,
        bool cursorPaging,
        CancellationToken ct)
    {
        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be 0 or greater.");

        if (limit is <= 0 or > 500)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be in range [1..500].");

        offset = PagingLimits.BoundOffset(offset);

        var trashMode = NormalizeTrashMode(trash);

        await uow.EnsureConnectionOpenAsync(ct);

        var trimmed = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var hasSearch = trimmed is not null;
        var like = hasSearch ? $"%{trimmed}%" : string.Empty;
        var useSeek = cursor is
        {
            AfterDateUtc: not null,
            AfterCreatedAtUtc: not null,
            AfterId: not null
        };

        var args = new
        {
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            Like = like,
            DateFromUtc = dateFrom is { } from
                ? DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null,
            DateToExclusiveUtc = dateTo is { } to && to < DateOnly.MaxValue
                ? DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null,
            TrashMode = trashMode,
            Limit = cursorPaging && limit < int.MaxValue ? limit + 1 : limit,
            Offset = offset,
            KnownTotal = cursor?.Total,
            AfterDateUtc = cursor?.AfterDateUtc,
            AfterCreatedAtUtc = cursor?.AfterCreatedAtUtc,
            AfterId = cursor?.AfterId
        };

        const string filtersSql = """
    d.type_code = @TypeCode
    AND (CAST(@DateFromUtc AS timestamp with time zone) IS NULL OR d.date_utc >= @DateFromUtc)
    AND (CAST(@DateToExclusiveUtc AS timestamp with time zone) IS NULL OR d.date_utc < @DateToExclusiveUtc)
    AND (
        CAST(@TrashMode AS text) = 'all'
        OR (CAST(@TrashMode AS text) = 'active' AND d.status <> 3 AND d.marked_for_deletion_at_utc IS NULL)
        OR (CAST(@TrashMode AS text) = 'deleted' AND (d.status = 3 OR d.marked_for_deletion_at_utc IS NOT NULL))
    )
""";

        var searchCandidatesSql = hasSearch
            ? """
WITH search_candidates AS (
    SELECT d.id AS document_id
      FROM documents d
     WHERE d.type_code = @TypeCode
       AND d.number ILIKE @Like
    UNION
    SELECT g.document_id
      FROM doc_general_journal_entry g
     WHERE g.reason_code ILIKE @Like
    UNION
    SELECT g.document_id
      FROM doc_general_journal_entry g
     WHERE g.memo ILIKE @Like
    UNION
    SELECT g.document_id
      FROM doc_general_journal_entry g
     WHERE g.external_reference ILIKE @Like
)
"""
            : string.Empty;
        var searchJoinSql = hasSearch
            ? "INNER JOIN search_candidates s ON s.document_id = d.id"
            : string.Empty;

        var countSql = $"""
{searchCandidatesSql}
SELECT COUNT(*)
FROM documents d
INNER JOIN doc_general_journal_entry g ON g.document_id = d.id
{searchJoinSql}
WHERE
{filtersSql};
""";

        var totalProjection = cursor is null
            ? "COUNT(*) OVER() AS TotalCount"
            : "@KnownTotal::integer AS TotalCount";
        var seekSql = useSeek
            ? "AND (d.date_utc, d.created_at_utc, d.id) < (@AfterDateUtc::timestamptz, @AfterCreatedAtUtc::timestamptz, @AfterId::uuid)"
            : string.Empty;
        var offsetSql = useSeek ? string.Empty : "OFFSET @Offset";
        var pageSql = $"""
{searchCandidatesSql}
SELECT
    d.id AS Id,
    d.date_utc AS DateUtc,
    d.created_at_utc AS CreatedAtUtc,
    d.number AS Number,
    CONCAT('General Journal Entry', CASE WHEN NULLIF(d.number, '') IS NOT NULL THEN ' ' || d.number ELSE '' END, ' ', TO_CHAR((d.date_utc AT TIME ZONE 'UTC')::date, 'FMMM/FMDD/YYYY')) AS Display,
    d.status AS DocumentStatus,
    (d.status = 3) AS IsMarkedForDeletion,
    g.journal_type AS JournalType,
    g.source AS Source,
    g.approval_state AS ApprovalState,
    g.reason_code AS ReasonCode,
    g.memo AS Memo,
    g.external_reference AS ExternalReference,
    g.auto_reverse AS AutoReverse,
    g.auto_reverse_on_utc AS AutoReverseOnUtc,
    g.reversal_of_document_id AS ReversalOfDocumentId,
    g.posted_by AS PostedBy,
    g.posted_at_utc AS PostedAtUtc,
    {totalProjection}
FROM documents d
INNER JOIN doc_general_journal_entry g ON g.document_id = d.id
{searchJoinSql}
WHERE
{filtersSql}
{seekSql}
ORDER BY d.date_utc DESC, d.created_at_utc DESC, d.id DESC
LIMIT @Limit {offsetSql};
""";

        var rows = (await uow.Connection.QueryAsync<Row>(
            new CommandDefinition(
                pageSql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();
        var total = cursor?.Total ?? (rows.Count == 0 ? 0 : rows[0].TotalCount);

        // A window count has no carrier row when an offset lies beyond the result.
        // Keep the exact-total contract with a count-only fallback for that rare case.
        if (cursor is null && rows.Count == 0 && offset > 0)
        {
            total = await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                countSql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct));
        }

        var hasMore = cursorPaging && rows.Count > limit;
        var visibleRows = rows.Take(limit).ToArray();
        var last = visibleRows.LastOrDefault();

        return new GeneralJournalEntryPageRecord(
            visibleRows.Select(Map).ToArray(),
            offset,
            limit,
            total,
            hasMore,
            last?.DateUtc,
            last?.CreatedAtUtc,
            last?.Id);
    }

    private static string NormalizeTrashMode(string? trash)
    {
        var value = (trash ?? string.Empty).Trim().ToLowerInvariant();

        if (value.Length == 0 || value == "active")
            return "active";

        if (value == "deleted")
            return "deleted";

        if (value == "all")
            return "all";

        throw new NgbArgumentInvalidException(nameof(trash), "Trash filter must be one of: active, deleted, all.");
    }

    private static GeneralJournalEntryListItemRecord Map(Row row)
        => new(
            row.Id,
            row.DateUtc,
            row.Number,
            row.Display,
            (DocumentStatus)row.DocumentStatus,
            row.IsMarkedForDeletion,
            (GeneralJournalEntryModels.JournalType)row.JournalType,
            (GeneralJournalEntryModels.Source)row.Source,
            (GeneralJournalEntryModels.ApprovalState)row.ApprovalState,
            row.ReasonCode,
            row.Memo,
            row.ExternalReference,
            row.AutoReverse,
            row.AutoReverseOnUtc,
            row.ReversalOfDocumentId,
            row.PostedBy,
            row.PostedAtUtc);

    private sealed class Row
    {
        public Guid Id { get; init; }
        public DateTime DateUtc { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public string? Number { get; init; }
        public string? Display { get; init; }
        public short DocumentStatus { get; init; }
        public bool IsMarkedForDeletion { get; init; }
        public short JournalType { get; init; }
        public short Source { get; init; }
        public short ApprovalState { get; init; }
        public string? ReasonCode { get; init; }
        public string? Memo { get; init; }
        public string? ExternalReference { get; init; }
        public bool AutoReverse { get; init; }
        public DateOnly? AutoReverseOnUtc { get; init; }
        public Guid? ReversalOfDocumentId { get; init; }
        public string? PostedBy { get; init; }
        public DateTime? PostedAtUtc { get; init; }
        public int TotalCount { get; init; }
    }
}
