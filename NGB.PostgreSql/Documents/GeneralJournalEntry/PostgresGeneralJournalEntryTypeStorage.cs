using Dapper;
using NGB.Accounting.Documents;
using NGB.Core.Documents;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Documents.GeneralJournalEntry;

/// <summary>
/// Typed storage hook for General Journal Entry.
///
/// DocumentDraftService creates the row in the common registry (documents) and then calls this storage
/// to initialize type-specific tables.
/// </summary>
public sealed class PostgresGeneralJournalEntryTypeStorage(IUnitOfWork uow, TimeProvider timeProvider)
    : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
{
    public string TypeCode => AccountingDocumentTypeCodes.GeneralJournalEntry;

    public async Task UpdateDraftAsync(DocumentRecord updatedDraft, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        // Keep the typed header's audit timestamps in sync with the common registry.
        // The typed header does not mirror number/date/status, but UpdatedAtUtc consistency is useful
        // for UI caches and optimistic refresh.
        const string sql = """
UPDATE doc_general_journal_entry
SET updated_at_utc = @updatedAtUtc
WHERE document_id = @documentId;
""";

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { documentId = updatedDraft.Id, updatedAtUtc = updatedDraft.UpdatedAtUtc },
            uow.Transaction,
            cancellationToken: ct)
        );
    }

    public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        var now = timeProvider.GetUtcNowDateTime();

        const string sql = """
INSERT INTO doc_general_journal_entry (
    document_id,
    journal_type,
    source,
    approval_state,
    auto_reverse,
    auto_reverse_on_utc,
    reversal_of_document_id,
    initiated_by,
    initiated_at_utc,
    created_at_utc,
    updated_at_utc
) VALUES (
    @documentId,
    1, -- Standard
    1, -- Manual
    1, -- Draft
    FALSE,
    NULL,
    NULL,
    NULL,
    NULL,
    @now,
    @now
)
ON CONFLICT (document_id) DO NOTHING;
""";

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { documentId, now },
            uow.Transaction,
            cancellationToken: ct)
        );
    }

    public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        // Header has ON DELETE CASCADE to lines + allocations.
        const string sql = "DELETE FROM doc_general_journal_entry WHERE document_id = @documentId;";
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { documentId },
            uow.Transaction,
            cancellationToken: ct)
        );
    }
}
