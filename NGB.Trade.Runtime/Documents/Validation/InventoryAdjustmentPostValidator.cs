using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class InventoryAdjustmentPostValidator(
    ITradeDocumentReaders readers,
    ICatalogService catalogs,
    TradeInventoryAvailabilityService inventoryAvailability)
    : IDocumentPostValidator
{
    public string TypeCode => TradeCodes.InventoryAdjustment;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(InventoryAdjustmentPostValidator));

        var head = await readers.ReadInventoryAdjustmentHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadInventoryAdjustmentLinesAsync(documentForUpdate.Id, ct);

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Inventory Adjustment must contain at least one line.");

        await TradeCatalogValidationGuards.EnsureWarehouseAsync(head.WarehouseId, "warehouse_id", catalogs, ct);
        await TradeCatalogValidationGuards.EnsureInventoryAdjustmentReasonAsync(head.ReasonId, "reason_id", catalogs, ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}]";

            await TradeCatalogValidationGuards.EnsureInventoryItemAsync(line.ItemId, $"{prefix}.item_id", catalogs, ct);

            if (line.QuantityDelta == 0m)
                throw new NgbArgumentInvalidException($"{prefix}.quantity_delta", "Quantity Delta must not be zero.");

            if (line.UnitCost <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.unit_cost", "Unit Cost must be greater than zero.");

            if (line.LineAmount <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.line_amount", "Line Amount must be greater than zero.");

            var expectedAmount = decimal.Round(Math.Abs(line.QuantityDelta) * line.UnitCost, 4, MidpointRounding.AwayFromZero);
            var actualAmount = decimal.Round(line.LineAmount, 4, MidpointRounding.AwayFromZero);

            if (expectedAmount != actualAmount)
            {
                throw new NgbArgumentInvalidException(
                    $"{prefix}.line_amount",
                    $"Line Amount must equal abs(Quantity Delta) x Unit Cost ({expectedAmount:0.####}).");
            }
        }

        await inventoryAvailability.EnsureSufficientOnHandAsync(
            head.DocumentDateUtc,
            lines
                .Where(static line => line.QuantityDelta < 0m)
                .Select(line => new TradeInventoryWithdrawalRequest(head.WarehouseId, line.ItemId, Math.Abs(line.QuantityDelta)))
                .ToArray(),
            ct);
    }
}
