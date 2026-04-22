using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Reference Register posting handler for pm.work_order.
///
/// Why this exists:
/// - Enables the standard document workflow states (Draft/Posted) for work orders.
/// - Allows Post/Unpost/Repost operations to run through the platform posting pipeline
///   even though pm.work_order currently has no accounting/OR/RR side effects.
///
/// Notes:
/// - This handler intentionally produces no records.
/// - Business invariants are enforced via draft/post validators.
/// </summary>
public sealed class WorkOrderReferenceRegisterPostingHandler : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.WorkOrder;

    public Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
        => Task.CompletedTask;
}
