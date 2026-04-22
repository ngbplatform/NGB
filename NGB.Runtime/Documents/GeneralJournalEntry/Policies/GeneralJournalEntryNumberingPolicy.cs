using NGB.Accounting.Documents;
using NGB.Definitions.Documents.Numbering;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Policies;

public sealed class GeneralJournalEntryNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => AccountingDocumentTypeCodes.GeneralJournalEntry;

    // GJE follows the standard document UX: draft gets its business number immediately,
    // and later workflow transitions must keep that number stable.
    public bool EnsureNumberOnCreateDraft => true;

    public bool EnsureNumberOnPost => false;
}
