using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentServicePostValidator(IEquipmentServiceReader reader) : IDocumentPostValidator
{
    public string TypeCode => EquipmentServiceDefinitions.DocumentCode;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        await ReadCompleteAsync(documentForUpdate.Id, ct);
    }

    public async Task<EquipmentServiceDetails> ReadCompleteAsync(Guid documentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var service = await reader.ReadAsync(documentId, ct);

        if (service?.EquipmentId is not { } equipmentId || equipmentId == Guid.Empty)
        {
            throw new NgbArgumentInvalidException("equipment_id", "Select equipment before posting the service.");
        }

        if (service.Hours is not > 0 or > 24)
        {
            throw new NgbArgumentInvalidException("hours", "Service hours must be greater than zero and at most 24.");
        }

        if (service.Amount is not > 0)
        {
            throw new NgbArgumentInvalidException("amount", "Service cost must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(service.Condition))
        {
            throw new NgbArgumentInvalidException("condition", "Record the equipment condition after service.");
        }

        return service;
    }
}
