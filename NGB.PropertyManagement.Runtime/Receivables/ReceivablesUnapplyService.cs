using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.UnitOfWork;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// Canonical receivables business action for reversing an existing allocation.
///
/// Implementation intentionally reuses the platform document lifecycle:
/// - validate that the target document is really <c>pm.receivable_apply</c>;
/// - read typed head fields for the response envelope;
/// - execute standard document unpost lifecycle.
///
/// No new apply reversal mechanism is introduced here.
/// </summary>
public sealed class ReceivablesUnapplyService(
    IDocumentService documentService,
    IDocumentPostingService posting,
    IPropertyManagementDocumentReaders readers,
    IUnitOfWork uow)
    : IReceivablesUnapplyService
{
    public async Task<ReceivablesUnapplyResponse> ExecuteAsync(Guid applyId, CancellationToken ct = default)
    {
        if (applyId == Guid.Empty)
            throw ReceivablesRequestValidationException.ApplyRequired();

        // Safety first: ensure the id belongs to pm.receivable_apply before touching workflow state.
        // IDocumentService.UnpostAsync(documentType, id) validates the type only AFTER unposting,
        // which is too late for a business-oriented endpoint.
        var doc = await documentService.GetByIdAsync(PropertyManagementCodes.ReceivableApply, applyId, ct);
        if (doc.Status != DocumentStatus.Posted)
        {
            throw new DocumentWorkflowStateMismatchException(
                operation: "Document.Unpost",
                documentId: applyId,
                expectedState: nameof(DocumentStatus.Posted),
                actualState: doc.Status.ToString());
        }

        PmReceivableApplyHead head = null!;
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            head = await readers.ReadReceivableApplyHeadAsync(applyId, innerCt);
        }, ct);

        await posting.UnpostAsync(applyId, ct);

        return new ReceivablesUnapplyResponse(
            ApplyId: applyId,
            CreditDocumentId: head.CreditDocumentId,
            ChargeDocumentId: head.ChargeDocumentId,
            AppliedOnUtc: head.AppliedOnUtc,
            UnappliedAmount: head.Amount);
    }
}
