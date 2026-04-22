using Dapper;
using NGB.Accounting.Documents;
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
    {
        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be 0 or greater.");

        if (limit is <= 0 or > 500)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be in range [1..500].");

        var trashMode = NormalizeTrashMode(trash);

        await uow.EnsureConnectionOpenAsync(ct);

        var trimmed = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var hasSearch = trimmed is not null;
        var like = hasSearch ? $"%{trimmed}%" : string.Empty;

        var args = new
        {
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            HasSearch = hasSearch,
            Like = like,
            DateFrom = dateFrom,
            DateTo = dateTo,
            TrashMode = trashMode,
            Limit = limit,
            Offset = offset
        };

        const string filtersSql = """
    d.type_code = @TypeCode
    AND (
        @HasSearch = FALSE
        OR COALESCE(d.number, '') ILIKE @Like
        OR COALESCE(g.reason_code, '') ILIKE @Like
        OR COALESCE(g.memo, '') ILIKE @Like
        OR COALESCE(g.external_reference, '') ILIKE @Like
    )
    AND (CAST(@DateFrom AS date) IS NULL OR (d.date_utc AT TIME ZONE 'UTC')::date >= CAST(@DateFrom AS date))
    AND (CAST(@DateTo AS date) IS NULL OR (d.date_utc AT TIME ZONE 'UTC')::date <= CAST(@DateTo AS date))
    AND (
        CAST(@TrashMode AS text) = 'all'
        OR (CAST(@TrashMode AS text) = 'active' AND d.status <> 3 AND d.marked_for_deletion_at_utc IS NULL)
        OR (CAST(@TrashMode AS text) = 'deleted' AND (d.status = 3 OR d.marked_for_deletion_at_utc IS NOT NULL))
    )
""";

        var countSql = $"""
SELECT COUNT(*)
FROM documents d
INNER JOIN doc_general_journal_entry g ON g.document_id = d.id
WHERE
{filtersSql};
""";

        var total = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                countSql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct));

        var pageSql = $"""
SELECT
    d.id AS Id,
    d.date_utc AS DateUtc,
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
    g.posted_at_utc AS PostedAtUtc
FROM documents d
INNER JOIN doc_general_journal_entry g ON g.document_id = d.id
WHERE
{filtersSql}
ORDER BY d.date_utc DESC, d.created_at_utc DESC, d.id DESC
LIMIT @Limit OFFSET @Offset;
""";

        var rows = await uow.Connection.QueryAsync<Row>(
            new CommandDefinition(
                pageSql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct));

        return new GeneralJournalEntryPageRecord(rows.Select(Map).ToArray(), offset, limit, total);
    }

    private static string NormalizeTrashMode(string? trash)
    {
        var value = (trash ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "deleted" => "deleted",
            "all" => "all",
            "" or "active" => "active",
            _ => throw new NgbArgumentInvalidException(nameof(trash), "Trash filter must be one of: active, deleted, all."),
        };
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
    }
}
