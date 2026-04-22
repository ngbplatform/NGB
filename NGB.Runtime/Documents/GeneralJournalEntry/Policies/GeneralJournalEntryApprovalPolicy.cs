using NGB.Accounting.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Definitions.Documents.Approval;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.Documents.Workflow;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Policies;

/// <summary>
/// Platform rule: manual General Journal Entries must be approved before posting.
/// System reversals are stored as GJE with Source=System and are pre-approved by DB constraint.
/// </summary>
public sealed class GeneralJournalEntryApprovalPolicy(IGeneralJournalEntryRepository gje)
    : IDocumentApprovalPolicy
{
    public string TypeCode => AccountingDocumentTypeCodes.GeneralJournalEntry;

    public async Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default)
    {
        const string op = "DocumentApprovalPolicy.EnsureCanPost";

        if (!string.Equals(documentForUpdate.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
            throw new DocumentTypeMismatchException(documentForUpdate.Id, expectedTypeCode: TypeCode, actualTypeCode: documentForUpdate.TypeCode);

        // Inside posting workflows we normally hold an explicit document lock and an active transaction.
        // Use FOR UPDATE read to observe the same row version and avoid racy reads.
        var header = await gje.GetHeaderForUpdateAsync(documentForUpdate.Id, ct);
        if (header is null)
            throw new GeneralJournalEntryTypedHeaderNotFoundException(op, documentForUpdate.Id);

        if (header.ApprovalState != GeneralJournalEntryModels.ApprovalState.Approved)
            throw new DocumentWorkflowStateMismatchException(
                operation: op,
                documentId: documentForUpdate.Id,
                expectedState: nameof(GeneralJournalEntryModels.ApprovalState.Approved),
                actualState: header.ApprovalState.ToString());
    }
}
