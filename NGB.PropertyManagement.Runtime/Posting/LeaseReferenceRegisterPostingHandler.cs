using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Reference Register posting handler for pm.lease.
///
/// Why this exists:
/// - Enables the standard document workflow states (Draft/Posted) for leases.
/// - Allows Post/Unpost/Repost operations to run through the platform posting pipeline
///   (even if pm.lease currently has no accounting/OR/RR side effects).
///
/// Notes:
/// - This handler intentionally produces no records.
/// - Business invariants (e.g., no-overlap) are enforced via Post validators (next patch).
/// </summary>
public sealed class LeaseReferenceRegisterPostingHandler : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.Lease;

    public Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
        => Task.CompletedTask;
}
