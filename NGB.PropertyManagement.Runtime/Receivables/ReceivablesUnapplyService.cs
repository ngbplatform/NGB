using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.WorkCenter;
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
    IUnitOfWork uow,
    IReceivablePaymentWorkCenterSynchronizer workCenter)
    : IReceivablesUnapplyService
{
    public async Task<ReceivablesUnapplyResponse> ExecuteAsync(Guid applyId, CancellationToken ct = default)
    {
        if (applyId == Guid.Empty)
            throw ReceivablesRequestValidationException.ApplyRequired();

        // Safety first: ensure the id belongs to pm.receivable_apply before touching workflow state.
        // The low-level posting port does not validate a caller-supplied type code,
        // so this business workflow validates its target before changing posting state.
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

        var changedUsers = await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await posting.UnpostAsync(applyId, manageTransaction: false, innerCt);
            return (IReadOnlyCollection<Guid>)(await workCenter.SynchronizeAsync(
                head.CreditDocumentId,
                correlationId: Guid.CreateVersion7(),
                causationId: applyId,
                innerCt));
        }, ct);

        await workCenter.NotifyChangedAsync(changedUsers, ct);

        return new ReceivablesUnapplyResponse(
            ApplyId: applyId,
            CreditDocumentId: head.CreditDocumentId,
            ChargeDocumentId: head.ChargeDocumentId,
            AppliedOnUtc: head.AppliedOnUtc,
            UnappliedAmount: head.Amount);
    }
}
