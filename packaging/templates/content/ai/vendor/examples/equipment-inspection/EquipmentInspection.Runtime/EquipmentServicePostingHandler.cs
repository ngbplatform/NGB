using EquipmentInspection.Definitions;
using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentServicePostingHandler(EquipmentServicePostValidator validator, IDimensionSetService dimensions)
    : IDocumentPostingHandler, IDocumentOperationalRegisterPostingHandler, IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => EquipmentServiceDefinitions.DocumentCode;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext context, CancellationToken ct)
    {
        var service = await validator.ReadCompleteAsync(document.Id, ct);
        var chart = await context.GetChartOfAccountsAsync(ct);
        context.Post(document.Id, document.DateUtc,
            chart.Get(EquipmentServiceDefinitions.ExpenseAccountCode), chart.Get(EquipmentServiceDefinitions.PayableAccountCode),
            service.Amount!.Value, debitDimensions: EquipmentDimensions(service));
    }

    public async Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
    {
        var service = await validator.ReadCompleteAsync(document.Id, ct);
        var dimensionSet = await dimensions.GetOrCreateIdAsync(EquipmentDimensions(service), ct);
        builder.Add(EquipmentServiceDefinitions.RegisterCode, new OperationalRegisterMovement(
            document.Id, document.DateUtc, dimensionSet, new Dictionary<string, decimal> { ["hours"] = service.Hours!.Value }));
    }

    public async Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (operation == ReferenceRegisterWriteOperation.Unpost)
            return;

        var service = await validator.ReadCompleteAsync(document.Id, ct);
        var dimensionSet = await dimensions.GetOrCreateIdAsync(EquipmentDimensions(service), ct);
        builder.Add(EquipmentConditionDefinitions.RegisterCode, new ReferenceRegisterRecordWrite(
            dimensionSet, PeriodUtc: null, RecorderDocumentId: document.Id,
            Values: new Dictionary<string, object?> { ["condition"] = service.Condition }));
    }

    private static DimensionBag EquipmentDimensions(EquipmentServiceDetails service)
        => new([new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, service.EquipmentId!.Value)]);
}
