using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters.Contracts;

namespace EquipmentInspection.Runtime;

/// <summary>
/// Inspection completion uses the document lifecycle without register records.
/// The registered handler enables that lifecycle; Post validators still apply.
/// </summary>
public sealed class InspectionWorkflowPostingHandler : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => EquipmentDefinitions.InspectionCode;

    public Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
