using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Approval;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class ChecklistApprovalPolicy(IChecklistReader reader) : IDocumentApprovalPolicy
{
    public string TypeCode => ChecklistDefinitions.DocumentCode;

    public async Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var checklist = await reader.ReadAsync(documentForUpdate.Id, ct);
        if (checklist?.Reviewed != true)
        {
            throw new NgbArgumentInvalidException("reviewed", "Review the checklist before posting.");
        }
    }
}
