using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;

namespace EquipmentInspection.Runtime;

public sealed class ReinspectionSourceValidator(IInspectionSourceReader sources) : IDocumentPostValidator
{
    public string TypeCode => EquipmentDefinitions.InspectionCode;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        if (!await sources.CanPostReinspectionAsync(documentForUpdate.Id, ct))
            throw new InspectionSourceUnavailableException();
    }
}
