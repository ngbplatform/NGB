using EquipmentInspection.Definitions;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentConditionPostingHandler(EquipmentConditionPostValidator validator, IDimensionSetService dimensions)
    : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => EquipmentConditionDefinitions.DocumentCode;

    public async Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // SubordinateToRecorder lets the engine tombstone the recorder's keys on Unpost.
        if (operation == ReferenceRegisterWriteOperation.Unpost)
            return;

        var condition = await validator.ReadCompleteAsync(document.Id, ct);
        var dimensionSet = await dimensions.GetOrCreateIdAsync(new DimensionBag(
            [new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, condition.EquipmentId!.Value)]), ct);
        builder.Add(EquipmentConditionDefinitions.RegisterCode, new ReferenceRegisterRecordWrite(
            dimensionSet, PeriodUtc: null, RecorderDocumentId: document.Id,
            Values: new Dictionary<string, object?> { ["condition"] = condition.Condition }));
    }
}
