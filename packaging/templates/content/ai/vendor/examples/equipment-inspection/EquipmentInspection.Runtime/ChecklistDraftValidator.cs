using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class ChecklistDraftValidator(TimeProvider timeProvider) : IDocumentDraftValidator
{
    public string TypeCode => ChecklistDefinitions.DocumentCode;

    public Task ValidateCreateDraftAsync(DocumentRecord draft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // A checklist records observations; scheduling belongs to an inspection.
        if (draft.DateUtc.Date > timeProvider.GetUtcNow().UtcDateTime.Date)
        {
            throw new NgbArgumentInvalidException("date", "A checklist cannot record a future observation date.");
        }

        return Task.CompletedTask;
    }
}
