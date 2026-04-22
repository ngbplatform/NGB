using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class VendorReturnPostValidator(
    ITradeDocumentReaders readers,
    ICatalogService catalogs,
    IDocumentRepository documents,
    TradeInventoryAvailabilityService inventoryAvailability)
    : IDocumentPostValidator
{
    public string TypeCode => TradeCodes.VendorReturn;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(VendorReturnPostValidator));

        var head = await readers.ReadVendorReturnHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadVendorReturnLinesAsync(documentForUpdate.Id, ct);

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Vendor Return must contain at least one line.");

        await TradeCatalogValidationGuards.EnsureVendorAsync(head.VendorId, "vendor_id", catalogs, ct);
        await TradeCatalogValidationGuards.EnsureWarehouseAsync(head.WarehouseId, "warehouse_id", catalogs, ct);

        if (head.PurchaseReceiptId is { } purchaseReceiptId)
        {
            await TradeDocumentReferenceValidationGuards.EnsurePostedPurchaseReceiptAsync(
                purchaseReceiptId,
                head.VendorId,
                readers,
                documents,
                ct);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}]";

            await TradeCatalogValidationGuards.EnsureInventoryItemAsync(line.ItemId, $"{prefix}.item_id", catalogs, ct);

            if (line.Quantity <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.quantity", "Quantity must be greater than zero.");

            if (line.UnitCost <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.unit_cost", "Unit Cost must be greater than zero.");

            if (line.LineAmount <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.line_amount", "Line Amount must be greater than zero.");

            var expectedAmount = decimal.Round(line.Quantity * line.UnitCost, 4, MidpointRounding.AwayFromZero);
            var actualAmount = decimal.Round(line.LineAmount, 4, MidpointRounding.AwayFromZero);

            if (expectedAmount != actualAmount)
            {
                throw new NgbArgumentInvalidException(
                    $"{prefix}.line_amount",
                    $"Line Amount must equal Quantity x Unit Cost ({expectedAmount:0.####}).");
            }
        }

        await inventoryAvailability.EnsureSufficientOnHandAsync(
            head.DocumentDateUtc,
            lines
                .Select(line => new TradeInventoryWithdrawalRequest(head.WarehouseId, line.ItemId, line.Quantity))
                .ToArray(),
            ct);
    }
}
