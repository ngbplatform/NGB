using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentConditionPostValidator(IEquipmentConditionReader reader) : IDocumentPostValidator
{
    public string TypeCode => EquipmentConditionDefinitions.DocumentCode;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        await ReadCompleteAsync(documentForUpdate.Id, ct);
    }

    public async Task<EquipmentConditionDetails> ReadCompleteAsync(Guid documentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var condition = await reader.ReadAsync(documentId, ct);

        if (condition?.EquipmentId is not { } equipmentId || equipmentId == Guid.Empty)
        {
            throw new NgbArgumentInvalidException("equipment_id", "Select equipment before posting the assessment.");
        }

        if (string.IsNullOrWhiteSpace(condition.Condition))
        {
            throw new NgbArgumentInvalidException("condition", "Record the equipment condition before posting.");
        }

        return condition;
    }
}
