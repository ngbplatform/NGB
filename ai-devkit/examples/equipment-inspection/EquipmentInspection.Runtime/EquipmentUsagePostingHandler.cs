using EquipmentInspection.Definitions;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.Dimensions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentUsagePostingHandler(EquipmentUsagePostValidator validator, IDimensionSetService dimensions)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => EquipmentUsageDefinitions.DocumentCode;

    public async Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
    {
        var usage = await validator.ReadCompleteAsync(document.Id, ct);
        var dimensionSet = await dimensions.GetOrCreateIdAsync(new DimensionBag(
            [new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, usage.EquipmentId!.Value)]), ct);
        builder.Add(EquipmentUsageDefinitions.RegisterCode, new OperationalRegisterMovement(
            document.Id, document.DateUtc, dimensionSet, new Dictionary<string, decimal> { ["hours"] = usage.Hours!.Value }));
    }
}
