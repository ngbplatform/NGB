namespace NGB.Accounting.Documents;

/// <summary>
/// Platform-level document type codes.
///
/// IMPORTANT:
/// - <c>documents.type_code</c> is used as a physical binding key for typed tables:
///     doc_{type_code}, doc_{type_code}__{part}, ...
/// - Therefore type codes are part of the database contract and must be stable.
/// - Use lowercase + underscores (portable across providers).
/// </summary>
public static class AccountingDocumentTypeCodes
{
    public const string GeneralJournalEntry = "general_journal_entry";
}
