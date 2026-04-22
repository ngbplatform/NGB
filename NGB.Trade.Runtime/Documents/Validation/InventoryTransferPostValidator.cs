using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class InventoryTransferPostValidator(
    ITradeDocumentReaders readers,
    ICatalogService catalogs,
    TradeInventoryAvailabilityService inventoryAvailability)
    : IDocumentPostValidator
{
    public string TypeCode => TradeCodes.InventoryTransfer;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(InventoryTransferPostValidator));

        var head = await readers.ReadInventoryTransferHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadInventoryTransferLinesAsync(documentForUpdate.Id, ct);

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Inventory Transfer must contain at least one line.");

        await TradeCatalogValidationGuards.EnsureWarehouseAsync(head.FromWarehouseId, "from_warehouse_id", catalogs, ct);
        await TradeCatalogValidationGuards.EnsureWarehouseAsync(head.ToWarehouseId, "to_warehouse_id", catalogs, ct);

        if (head.FromWarehouseId == head.ToWarehouseId)
        {
            throw new NgbArgumentInvalidException(
                "to_warehouse_id",
                "From Warehouse and To Warehouse must be different.");
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}]";

            await TradeCatalogValidationGuards.EnsureInventoryItemAsync(line.ItemId, $"{prefix}.item_id", catalogs, ct);

            if (line.Quantity <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.quantity", "Quantity must be greater than zero.");
        }

        await inventoryAvailability.EnsureSufficientOnHandAsync(
            head.DocumentDateUtc,
            lines
                .Select(line => new TradeInventoryWithdrawalRequest(head.FromWarehouseId, line.ItemId, line.Quantity))
                .ToArray(),
            ct);
    }
}
