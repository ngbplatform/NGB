using Dapper;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents.GeneralJournalEntry;

public sealed class PostgresGeneralJournalEntryRepository(IUnitOfWork uow) : IGeneralJournalEntryRepository
{
    private const string HeaderTable = "doc_general_journal_entry";
    private const string LinesTable = "doc_general_journal_entry__lines";
    private const string AllocationsTable = "doc_general_journal_entry__allocations";

    public async Task<GeneralJournalEntryHeaderRecord?> GetHeaderAsync(Guid documentId, CancellationToken ct = default)
    {
        const string sql = $"""
SELECT
    document_id AS DocumentId,
    journal_type AS JournalType,
    source AS Source,
    approval_state AS ApprovalState,
    reason_code AS ReasonCode,
    memo AS Memo,
    external_reference AS ExternalReference,
    auto_reverse AS AutoReverse,
    auto_reverse_on_utc AS AutoReverseOnUtc,
    reversal_of_document_id AS ReversalOfDocumentId,
    initiated_by AS InitiatedBy,
    initiated_at_utc AS InitiatedAtUtc,
    submitted_by AS SubmittedBy,
    submitted_at_utc AS SubmittedAtUtc,
    approved_by AS ApprovedBy,
    approved_at_utc AS ApprovedAtUtc,
    rejected_by AS RejectedBy,
    rejected_at_utc AS RejectedAtUtc,
    reject_reason AS RejectReason,
    posted_by AS PostedBy,
    posted_at_utc AS PostedAtUtc,
    created_at_utc AS CreatedAtUtc,
    updated_at_utc AS UpdatedAtUtc
FROM {HeaderTable}
WHERE document_id = @documentId;
""";

        return await uow.Connection.QuerySingleOrDefaultAsync<GeneralJournalEntryHeaderRecord>(
            new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<GeneralJournalEntryHeaderRecord?> GetHeaderForUpdateAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string sql = $"""
SELECT
    document_id AS DocumentId,
    journal_type AS JournalType,
    source AS Source,
    approval_state AS ApprovalState,
    reason_code AS ReasonCode,
    memo AS Memo,
    external_reference AS ExternalReference,
    auto_reverse AS AutoReverse,
    auto_reverse_on_utc AS AutoReverseOnUtc,
    reversal_of_document_id AS ReversalOfDocumentId,
    initiated_by AS InitiatedBy,
    initiated_at_utc AS InitiatedAtUtc,
    submitted_by AS SubmittedBy,
    submitted_at_utc AS SubmittedAtUtc,
    approved_by AS ApprovedBy,
    approved_at_utc AS ApprovedAtUtc,
    rejected_by AS RejectedBy,
    rejected_at_utc AS RejectedAtUtc,
    reject_reason AS RejectReason,
    posted_by AS PostedBy,
    posted_at_utc AS PostedAtUtc,
    created_at_utc AS CreatedAtUtc,
    updated_at_utc AS UpdatedAtUtc
FROM {HeaderTable}
WHERE document_id = @documentId
FOR UPDATE;
""";

        return await uow.Connection.QuerySingleOrDefaultAsync<GeneralJournalEntryHeaderRecord>(
            new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task UpsertHeaderAsync(GeneralJournalEntryHeaderRecord header, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string sql = $"""
INSERT INTO {HeaderTable} (
    document_id,
    journal_type,
    source,
    approval_state,
    reason_code,
    memo,
    external_reference,
    auto_reverse,
    auto_reverse_on_utc,
    reversal_of_document_id,
    initiated_by,
    initiated_at_utc,
    submitted_by,
    submitted_at_utc,
    approved_by,
    approved_at_utc,
    rejected_by,
    rejected_at_utc,
    reject_reason,
    posted_by,
    posted_at_utc,
    created_at_utc,
    updated_at_utc
) VALUES (
    @DocumentId,
    @JournalType,
    @Source,
    @ApprovalState,
    @ReasonCode,
    @Memo,
    @ExternalReference,
    @AutoReverse,
    @AutoReverseOnUtc,
    @ReversalOfDocumentId,
    @InitiatedBy,
    @InitiatedAtUtc,
    @SubmittedBy,
    @SubmittedAtUtc,
    @ApprovedBy,
    @ApprovedAtUtc,
    @RejectedBy,
    @RejectedAtUtc,
    @RejectReason,
    @PostedBy,
    @PostedAtUtc,
    @CreatedAtUtc,
    @UpdatedAtUtc
)
ON CONFLICT (document_id) DO UPDATE SET
    journal_type = EXCLUDED.journal_type,
    source = EXCLUDED.source,
    approval_state = EXCLUDED.approval_state,
    reason_code = EXCLUDED.reason_code,
    memo = EXCLUDED.memo,
    external_reference = EXCLUDED.external_reference,
    auto_reverse = EXCLUDED.auto_reverse,
    auto_reverse_on_utc = EXCLUDED.auto_reverse_on_utc,
    reversal_of_document_id = EXCLUDED.reversal_of_document_id,
    initiated_by = EXCLUDED.initiated_by,
    initiated_at_utc = EXCLUDED.initiated_at_utc,
    submitted_by = EXCLUDED.submitted_by,
    submitted_at_utc = EXCLUDED.submitted_at_utc,
    approved_by = EXCLUDED.approved_by,
    approved_at_utc = EXCLUDED.approved_at_utc,
    rejected_by = EXCLUDED.rejected_by,
    rejected_at_utc = EXCLUDED.rejected_at_utc,
    reject_reason = EXCLUDED.reject_reason,
    posted_by = EXCLUDED.posted_by,
    posted_at_utc = EXCLUDED.posted_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;
""";

        await uow.Connection.ExecuteAsync(new CommandDefinition(sql, header, uow.Transaction, cancellationToken: ct));
    }

    public async Task TouchUpdatedAtAsync(Guid documentId, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string sql = $"UPDATE {HeaderTable} SET updated_at_utc = @updatedAtUtc WHERE document_id = @documentId;";
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { documentId, updatedAtUtc },
            uow.Transaction,
            cancellationToken: ct)
        );
    }

    public async Task<IReadOnlyList<GeneralJournalEntryLineRecord>> GetLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        const string sql = $"""
SELECT
    document_id AS DocumentId,
    line_no AS LineNo,
    side AS Side,
    account_id AS AccountId,
    amount AS Amount,
    memo AS Memo,
    dimension_set_id AS DimensionSetId
FROM {LinesTable}
WHERE document_id = @documentId
ORDER BY line_no;
""";

        var rows = await uow.Connection.QueryAsync<GeneralJournalEntryLineRecord>(
            new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToList();
    }

    public async Task ReplaceLinesAsync(
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryLineRecord> lines,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string del = $"DELETE FROM {LinesTable} WHERE document_id = @documentId;";
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            del,
            new { documentId },
            uow.Transaction,
            cancellationToken: ct)
        );

        if (lines.Count == 0)
            return;

        const string ins = $"""
INSERT INTO {LinesTable} (
    document_id,
    line_no,
    side,
    account_id,
    amount,
    memo,
    dimension_set_id
) VALUES (
    @DocumentId,
    @LineNo,
    @Side,
    @AccountId,
    @Amount,
    @Memo,
    @DimensionSetId
);
""";

        await uow.Connection.ExecuteAsync(new CommandDefinition(ins, lines, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<GeneralJournalEntryAllocationRecord>> GetAllocationsAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        const string sql = $"""
SELECT
    document_id AS DocumentId,
    entry_no AS EntryNo,
    debit_line_no AS DebitLineNo,
    credit_line_no AS CreditLineNo,
    amount AS Amount
FROM {AllocationsTable}
WHERE document_id = @documentId
ORDER BY entry_no;
""";

        var rows = await uow.Connection.QueryAsync<GeneralJournalEntryAllocationRecord>(
            new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToList();
    }

    public async Task ReplaceAllocationsAsync(
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryAllocationRecord> allocations,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string del = $"DELETE FROM {AllocationsTable} WHERE document_id = @documentId;";
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            del,
            new { documentId },
            uow.Transaction,
            cancellationToken: ct)
        );

        if (allocations.Count == 0)
            return;

        const string ins = $"""
INSERT INTO {AllocationsTable} (
    document_id,
    entry_no,
    debit_line_no,
    credit_line_no,
    amount
) VALUES (
    @DocumentId,
    @EntryNo,
    @DebitLineNo,
    @CreditLineNo,
    @Amount
);
""";

        await uow.Connection.ExecuteAsync(new CommandDefinition(ins, allocations, uow.Transaction, cancellationToken: ct));
    }

    public async Task<Guid?> TryGetSystemReversalByOriginalAsync(Guid originalDocumentId, CancellationToken ct = default)
    {
        const string sql = $"""
SELECT document_id
FROM {HeaderTable}
WHERE reversal_of_document_id = @originalDocumentId
LIMIT 1;
""";

        return await uow.Connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, new { originalDocumentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Guid>> GetDueSystemReversalsAsync(
        DateOnly utcDate,
        int limit,
        CancellationToken ct = default)
    {
        var rows = await GetDueSystemReversalCandidatesAsync(utcDate, limit, ct: ct);
        return rows.Select(x => x.DocumentId).ToList();
    }

    public async Task<IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate>> GetDueSystemReversalCandidatesAsync(
        DateOnly utcDate,
        int limit,
        DateTime? afterDateUtc = null,
        Guid? afterDocumentId = null,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        if (afterDateUtc is null != afterDocumentId is null)
            throw new NgbArgumentRequiredException(afterDateUtc is null ? nameof(afterDateUtc) : nameof(afterDocumentId));

        // Due = documents.date_utc::date <= utcDate AND documents.status=Draft AND typed header indicates system reversal.
        const string sqlWithoutCursor = $"""
SELECT
    d.id AS DocumentId,
    d.date_utc AS DateUtc
FROM documents d
JOIN {HeaderTable} h ON h.document_id = d.id
WHERE d.status = 1 -- Draft
  AND d.date_utc::date <= @utcDate
  AND h.source = 2
  AND h.journal_type = 2
  AND h.approval_state = 3
  AND h.posted_at_utc IS NULL
ORDER BY d.date_utc, d.id
LIMIT @limit;
""";

        const string sqlWithCursor = $"""
SELECT
    d.id AS DocumentId,
    d.date_utc AS DateUtc
FROM documents d
JOIN {HeaderTable} h ON h.document_id = d.id
WHERE d.status = 1 -- Draft
  AND d.date_utc::date <= @utcDate
  AND h.source = 2
  AND h.journal_type = 2
  AND h.approval_state = 3
  AND h.posted_at_utc IS NULL
  AND (
        d.date_utc > @afterDateUtc
        OR (d.date_utc = @afterDateUtc AND d.id > @afterDocumentId)
      )
ORDER BY d.date_utc, d.id
LIMIT @limit;
""";

        var sql = afterDateUtc is null ? sqlWithoutCursor : sqlWithCursor;
        object args = afterDateUtc is null
            ? new { utcDate, limit }
            : new { utcDate, limit, afterDateUtc, afterDocumentId };

        var rows = await uow.Connection.QueryAsync<GeneralJournalEntryDueSystemReversalCandidate>(
            new CommandDefinition(
                sql,
                args,
                uow.Transaction,
                cancellationToken: ct));

        return rows.ToList();
    }
}
