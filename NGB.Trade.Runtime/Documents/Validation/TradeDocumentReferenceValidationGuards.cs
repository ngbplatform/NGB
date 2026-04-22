using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;

namespace NGB.Trade.Runtime.Documents.Validation;

internal static class TradeDocumentReferenceValidationGuards
{
    public static async Task EnsurePostedSalesInvoiceAsync(
        Guid salesInvoiceId,
        Guid expectedCustomerId,
        ITradeDocumentReaders readers,
        IDocumentRepository documents,
        CancellationToken ct)
    {
        var document = await documents.GetAsync(salesInvoiceId, ct);
        if (document is null || !string.Equals(document.TypeCode, TradeCodes.SalesInvoice, StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException("sales_invoice_id", "Referenced Sales Invoice is not available.");

        if (document.Status != DocumentStatus.Posted)
            throw new NgbArgumentInvalidException("sales_invoice_id", "Referenced Sales Invoice must be posted.");

        var invoice = await readers.ReadSalesInvoiceHeadAsync(salesInvoiceId, ct);
        if (invoice.CustomerId != expectedCustomerId)
        {
            throw new NgbArgumentInvalidException(
                "sales_invoice_id",
                "Referenced Sales Invoice must belong to the selected customer.");
        }
    }

    public static async Task EnsurePostedPurchaseReceiptAsync(
        Guid purchaseReceiptId,
        Guid expectedVendorId,
        ITradeDocumentReaders readers,
        IDocumentRepository documents,
        CancellationToken ct)
    {
        var document = await documents.GetAsync(purchaseReceiptId, ct);
        if (document is null
            || !string.Equals(document.TypeCode, TradeCodes.PurchaseReceipt, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbArgumentInvalidException("purchase_receipt_id", "Referenced Purchase Receipt is not available.");
        }

        if (document.Status != DocumentStatus.Posted)
            throw new NgbArgumentInvalidException("purchase_receipt_id", "Referenced Purchase Receipt must be posted.");

        var receipt = await readers.ReadPurchaseReceiptHeadAsync(purchaseReceiptId, ct);
        if (receipt.VendorId != expectedVendorId)
        {
            throw new NgbArgumentInvalidException(
                "purchase_receipt_id",
                "Referenced Purchase Receipt must belong to the selected vendor.");
        }
    }
}
