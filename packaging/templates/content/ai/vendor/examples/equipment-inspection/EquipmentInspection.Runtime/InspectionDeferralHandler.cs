using NGB.Application.Abstractions.Services;
using NGB.Runtime.Documents;

namespace EquipmentInspection.Runtime;

public sealed class InspectionDeferralHandler(IDocumentDraftService drafts) : IDocumentActionHandler
{
    public async Task<DocumentActionHandlerResult> ExecuteAsync(DocumentActionHandlerContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await drafts.UpdateDraftAsync(context.Document.Id, number: null,
            dateUtc: context.Document.DateUtc.AddDays(1), manageTransaction: false, ct: ct);
        return new DocumentActionHandlerResult();
    }
}
