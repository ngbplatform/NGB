using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Posting;

public sealed class InventoryTransferInventoryOperationalRegisterPostingHandler(
    ITradeDocumentReaders readers,
    ITradeAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => TradeCodes.InventoryTransfer;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var head = await readers.ReadInventoryTransferHeadAsync(document.Id, ct);
        var lines = await readers.ReadInventoryTransferLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var register = await registers.GetByIdAsync(policy.InventoryMovementsRegisterId, ct);

        if (register is null)
        {
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.InventoryMovementsRegisterId}' referenced by '{TradeCodes.AccountingPolicy}' was not found.");
        }

        var occurredAtUtc = TradePostingCommon.ToOccurredAtUtc(head.DocumentDateUtc);
        var bags = lines
            .SelectMany(line => new[]
            {
                TradePostingCommon.InventoryBag(line.ItemId, head.FromWarehouseId),
                TradePostingCommon.InventoryBag(line.ItemId, head.ToWarehouseId)
            })
            .ToArray();
        var dimensionSetIds = await dimensionSets.GetOrCreateIdsAsync(bags, ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var sourceDimensionSetId = dimensionSetIds[i * 2];
            var destinationDimensionSetId = dimensionSetIds[(i * 2) + 1];

            builder.Add(
                registerCode: register.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: sourceDimensionSetId,
                    Resources: TradePostingCommon.BuildInventoryMovementResources(-line.Quantity)));

            builder.Add(
                registerCode: register.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: destinationDimensionSetId,
                    Resources: TradePostingCommon.BuildInventoryMovementResources(line.Quantity)));
        }
    }
}
