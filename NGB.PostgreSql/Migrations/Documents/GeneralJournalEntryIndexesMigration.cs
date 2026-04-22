using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

public sealed class GeneralJournalEntryIndexesMigration : IDdlObject
{
    public string Name => "documents.general_journal_entry.indexes";

    public string Generate() => """
-- Only one system reversal per original document.
CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_gje_reversal_of_document_id
    ON doc_general_journal_entry (reversal_of_document_id)
    WHERE reversal_of_document_id IS NOT NULL;

-- Fast lookup for runner: due system reversals.
CREATE INDEX IF NOT EXISTS ix_doc_gje_system_reversals
    ON doc_general_journal_entry (source, journal_type, approval_state, posted_at_utc);

CREATE INDEX IF NOT EXISTS ix_doc_gje_lines_document_side
    ON doc_general_journal_entry__lines (document_id, side, line_no);

CREATE INDEX IF NOT EXISTS ix_doc_gje_alloc_debit_line
    ON doc_general_journal_entry__allocations (document_id, debit_line_no);

CREATE INDEX IF NOT EXISTS ix_doc_gje_alloc_credit_line
    ON doc_general_journal_entry__allocations (document_id, credit_line_no);
""";
}
