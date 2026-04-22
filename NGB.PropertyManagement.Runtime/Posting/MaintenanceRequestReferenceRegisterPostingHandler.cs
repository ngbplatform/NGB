using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Enables the standard Draft/Posted workflow for pm.maintenance_request without side effects.
/// The document is a business fact, but does not yet write accounting/OR/RR records.
/// </summary>
public sealed class MaintenanceRequestReferenceRegisterPostingHandler : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.MaintenanceRequest;

    public Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
        => Task.CompletedTask;
}
