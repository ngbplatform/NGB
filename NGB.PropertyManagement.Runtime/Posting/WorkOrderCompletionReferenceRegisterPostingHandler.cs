using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Enables the standard Draft/Posted workflow for pm.work_order_completion without RR side effects.
///
/// Relationship policy:
/// - the typed head field <c>work_order_id</c> is declaratively mirrored into <c>created_from</c>
///   so provenance is visible in Document Flow for draft/update/delete as well;
/// - on first post we still create an explicit <c>based_on</c> relationship to the target work order
///   because posting/explainability treats completion as an execution result based on that work order.
///
/// The two persisted edges are intentional and represent different semantics.
/// </summary>
public sealed class WorkOrderCompletionReferenceRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IDocumentRelationshipService relationships)
    : IDocumentReferenceRegisterPostingHandler
{
    private const string BasedOnRelationshipCode = "based_on";

    public string TypeCode => PropertyManagementCodes.WorkOrderCompletion;

    public async Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        if (operation == ReferenceRegisterWriteOperation.Post && document.Status == DocumentStatus.Draft)
        {
            var completion = await readers.ReadWorkOrderCompletionHeadAsync(document.Id, ct);
            await relationships.CreateAsync(
                fromDocumentId: document.Id,
                toDocumentId: completion.WorkOrderId,
                relationshipCode: BasedOnRelationshipCode,
                manageTransaction: false,
                ct: ct);
        }
    }
}
