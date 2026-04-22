using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class CustomerReturnPostValidator(
    ITradeDocumentReaders readers,
    ICatalogService catalogs,
    IDocumentRepository documents)
    : IDocumentPostValidator
{
    public string TypeCode => TradeCodes.CustomerReturn;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(CustomerReturnPostValidator));

        var head = await readers.ReadCustomerReturnHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadCustomerReturnLinesAsync(documentForUpdate.Id, ct);

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Customer Return must contain at least one line.");

        await TradeCatalogValidationGuards.EnsureCustomerAsync(head.CustomerId, "customer_id", catalogs, ct);
        await TradeCatalogValidationGuards.EnsureWarehouseAsync(head.WarehouseId, "warehouse_id", catalogs, ct);

        if (head.SalesInvoiceId is { } salesInvoiceId)
        {
            await TradeDocumentReferenceValidationGuards.EnsurePostedSalesInvoiceAsync(
                salesInvoiceId,
                head.CustomerId,
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

            if (line.UnitPrice <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.unit_price", "Unit Price must be greater than zero.");

            if (line.UnitCost <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.unit_cost", "Unit Cost Snapshot must be greater than zero.");

            if (line.LineAmount <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.line_amount", "Line Amount must be greater than zero.");

            var expectedAmount = decimal.Round(line.Quantity * line.UnitPrice, 4, MidpointRounding.AwayFromZero);
            var actualAmount = decimal.Round(line.LineAmount, 4, MidpointRounding.AwayFromZero);

            if (expectedAmount != actualAmount)
            {
                throw new NgbArgumentInvalidException(
                    $"{prefix}.line_amount",
                    $"Line Amount must equal Quantity x Unit Price ({expectedAmount:0.####}).");
            }
        }
    }
}
