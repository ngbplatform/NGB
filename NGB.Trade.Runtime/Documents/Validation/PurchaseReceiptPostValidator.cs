using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Trade.Documents;
using NGB.Tools.Exceptions;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class PurchaseReceiptPostValidator(ITradeDocumentReaders readers, ICatalogService catalogs)
    : IDocumentPostValidator
{
    public string TypeCode => TradeCodes.PurchaseReceipt;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(PurchaseReceiptPostValidator));

        var head = await readers.ReadPurchaseReceiptHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadPurchaseReceiptLinesAsync(documentForUpdate.Id, ct);

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Purchase Receipt must contain at least one line.");

        await TradeCatalogValidationGuards.EnsureVendorAsync(head.VendorId, "vendor_id", catalogs, ct);
        await TradeCatalogValidationGuards.EnsureWarehouseAsync(head.WarehouseId, "warehouse_id", catalogs, ct);

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
    }
}
