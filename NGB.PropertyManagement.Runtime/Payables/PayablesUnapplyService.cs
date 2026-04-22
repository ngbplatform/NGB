using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.UnitOfWork;

namespace NGB.PropertyManagement.Runtime.Payables;

public sealed class PayablesUnapplyService(
    IDocumentService documentService,
    IDocumentPostingService posting,
    IPropertyManagementDocumentReaders readers,
    IUnitOfWork uow)
    : IPayablesUnapplyService
{
    public async Task<PayablesUnapplyResponse> ExecuteAsync(Guid applyId, CancellationToken ct = default)
    {
        if (applyId == Guid.Empty)
            throw PayablesRequestValidationException.ApplyRequired();

        var doc = await documentService.GetByIdAsync(
            PropertyManagementCodes.PayableApply,
            applyId,
            ct);

        if (doc.Status != DocumentStatus.Posted)
        {
            throw new DocumentWorkflowStateMismatchException(
                operation: "Document.Unpost",
                documentId: applyId,
                expectedState: nameof(DocumentStatus.Posted),
                actualState: doc.Status.ToString());
        }

        PmPayableApplyHead head = null!;
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            head = await readers.ReadPayableApplyHeadAsync(applyId, innerCt);
        }, ct);

        await posting.UnpostAsync(applyId, ct);

        return new PayablesUnapplyResponse(
            applyId,
            head.CreditDocumentId,
            head.ChargeDocumentId,
            head.AppliedOnUtc,
            head.Amount);
    }
}
