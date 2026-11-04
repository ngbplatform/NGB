using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters.Contracts;

namespace EquipmentInspection.Runtime;

public sealed class ChecklistWorkflowPostingHandler : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => ChecklistDefinitions.DocumentCode;

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
