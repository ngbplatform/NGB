using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Posting;

public sealed class VendorReturnInventoryOperationalRegisterPostingHandler(
    ITradeDocumentReaders readers,
    ITradeAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => TradeCodes.VendorReturn;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var head = await readers.ReadVendorReturnHeadAsync(document.Id, ct);
        var lines = await readers.ReadVendorReturnLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var register = await registers.GetByIdAsync(policy.InventoryMovementsRegisterId, ct);

        if (register is null)
        {
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.InventoryMovementsRegisterId}' referenced by '{TradeCodes.AccountingPolicy}' was not found.");
        }

        var occurredAtUtc = TradePostingCommon.ToOccurredAtUtc(head.DocumentDateUtc);

        foreach (var line in lines)
        {
            var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(
                TradePostingCommon.InventoryBag(line.ItemId, head.WarehouseId),
                ct);

            builder.Add(
                registerCode: register.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: TradePostingCommon.BuildInventoryMovementResources(-line.Quantity)));
        }
    }
}
