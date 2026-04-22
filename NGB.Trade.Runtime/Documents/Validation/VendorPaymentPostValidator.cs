using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class VendorPaymentPostValidator(
    ITradeDocumentReaders readers,
    ICatalogService catalogs,
    IDocumentRepository documents,
    IChartOfAccountsProvider charts)
    : IDocumentPostValidator
{
    public string TypeCode => TradeCodes.VendorPayment;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(VendorPaymentPostValidator));

        var head = await readers.ReadVendorPaymentHeadAsync(documentForUpdate.Id, ct);

        await TradeCatalogValidationGuards.EnsureVendorAsync(head.VendorId, "vendor_id", catalogs, ct);

        if (head.Amount <= 0m)
            throw new NgbArgumentInvalidException("amount", "Amount must be greater than zero.");

        if (head.CashAccountId is { } cashAccountId)
            await TradeAccountingValidationGuards.EnsureCashAccountAsync(cashAccountId, "cash_account_id", charts, ct);

        if (head.PurchaseReceiptId is { } purchaseReceiptId)
        {
            await TradeDocumentReferenceValidationGuards.EnsurePostedPurchaseReceiptAsync(
                purchaseReceiptId,
                head.VendorId,
                readers,
                documents,
                ct);
        }
    }
}
