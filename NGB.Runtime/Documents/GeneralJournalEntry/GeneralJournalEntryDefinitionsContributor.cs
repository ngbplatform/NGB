using NGB.Accounting.Documents;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.GeneralJournalEntry.Policies;

namespace NGB.Runtime.Documents.GeneralJournalEntry;

/// <summary>
/// Platform-level definition for General Journal Entry (GJE).
/// Keeps the platform document discoverable via Definitions and enables policy-driven orchestration.
/// </summary>
public sealed class GeneralJournalEntryDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocument(
            AccountingDocumentTypeCodes.GeneralJournalEntry,
            d => d
                .Metadata(BuildMetadata())
                .NumberingPolicy<GeneralJournalEntryNumberingPolicy>()
                .ApprovalPolicy<GeneralJournalEntryApprovalPolicy>());
    }

    private static DocumentTypeMetadata BuildMetadata()
        => new(
            TypeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_general_journal_entry",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("journal_type", ColumnType.Int32, Required: true),
                        new("source", ColumnType.Int32, Required: true),
                        new("approval_state", ColumnType.Int32, Required: true),
                        new("reason_code", ColumnType.String),
                        new("memo", ColumnType.String),
                        new("external_reference", ColumnType.String),
                        new("auto_reverse", ColumnType.Boolean, Required: true),
                        new("auto_reverse_on_utc", ColumnType.Date),
                        new("reversal_of_document_id", ColumnType.Guid),
                        new("initiated_by", ColumnType.String),
                        new("initiated_at_utc", ColumnType.DateTimeUtc),
                        new("submitted_by", ColumnType.String),
                        new("submitted_at_utc", ColumnType.DateTimeUtc),
                        new("approved_by", ColumnType.String),
                        new("approved_at_utc", ColumnType.DateTimeUtc),
                        new("rejected_by", ColumnType.String),
                        new("rejected_at_utc", ColumnType.DateTimeUtc),
                        new("reject_reason", ColumnType.String),
                        new("posted_by", ColumnType.String),
                        new("posted_at_utc", ColumnType.DateTimeUtc),
                        new("created_at_utc", ColumnType.DateTimeUtc, Required: true),
                        new("updated_at_utc", ColumnType.DateTimeUtc, Required: true),
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_general_journal_entry__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("line_no", ColumnType.Int32, Required: true),
                        new("side", ColumnType.Int32, Required: true),
                        new("account_id", ColumnType.Guid, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String),
                        new("dimension_set_id", ColumnType.Guid, Required: true),
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_general_journal_entry__allocations",
                    Kind: TableKind.Part,
                    PartCode: "allocations",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("entry_no", ColumnType.Int32, Required: true),
                        new("debit_line_no", ColumnType.Int32, Required: true),
                        new("credit_line_no", ColumnType.Int32, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                    ]),
            ],
            Presentation: new DocumentPresentationMetadata("General Journal Entry"),
            Version: new DocumentMetadataVersion(1, "platform"));
}
