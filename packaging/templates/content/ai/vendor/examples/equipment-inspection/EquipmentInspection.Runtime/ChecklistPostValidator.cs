using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Runtime.Documents.Policies;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class ChecklistPostValidator(IChecklistReader reader, IDocumentApprovalPolicyResolver approvalPolicies)
    : IDocumentPostValidator
{
    public string TypeCode => ChecklistDefinitions.DocumentCode;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var checklist = await reader.ReadAsync(documentForUpdate.Id, ct);
        if (checklist is null || checklist.LineCount == 0 || checklist.IncompleteCount > 0)
        {
            throw new NgbArgumentInvalidException("checks", "Complete at least one check before posting.");
        }

        // Generic Post calls post validators; the application explicitly invokes its approval policy.
        var policy = approvalPolicies.Resolve(TypeCode)
            ?? throw new NgbConfigurationViolationException("The checklist requires a registered approval policy.");
        await policy.EnsureCanPostAsync(documentForUpdate, ct);
    }
}
