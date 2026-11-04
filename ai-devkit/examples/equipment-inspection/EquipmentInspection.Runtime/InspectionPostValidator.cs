using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class InspectionPostValidator(IInspectionReader reader) : IDocumentPostValidator
{
    public string TypeCode => EquipmentDefinitions.InspectionCode;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var inspection = await reader.ReadAsync(documentForUpdate.Id, ct);

        if (inspection?.EquipmentId is not { } equipmentId || equipmentId == Guid.Empty)
        {
            throw new NgbArgumentInvalidException("equipment_id", "Select equipment before posting the inspection.");
        }

        if (string.IsNullOrWhiteSpace(inspection.Result))
        {
            throw new NgbArgumentInvalidException("result", "Record the inspection result before posting.");
        }
    }
}
