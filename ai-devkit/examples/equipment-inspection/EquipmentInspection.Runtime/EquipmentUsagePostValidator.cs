using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentUsagePostValidator(IEquipmentUsageReader reader) : IDocumentPostValidator
{
    public string TypeCode => EquipmentUsageDefinitions.DocumentCode;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        await ReadCompleteAsync(documentForUpdate.Id, ct);
    }

    public async Task<EquipmentUsageDetails> ReadCompleteAsync(Guid documentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var usage = await reader.ReadAsync(documentId, ct);
        if (usage?.EquipmentId is not { } equipmentId || equipmentId == Guid.Empty)
        {
            throw new NgbArgumentInvalidException("equipment_id", "Select equipment before posting usage.");
        }

        if (usage.Hours is not > 0 or > 24)
        {
            throw new NgbArgumentInvalidException("hours", "Usage must be greater than zero and at most 24 hours.");
        }

        return usage;
    }
}
