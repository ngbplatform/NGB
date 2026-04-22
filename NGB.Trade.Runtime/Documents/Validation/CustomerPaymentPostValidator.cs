using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class CustomerPaymentPostValidator(
    ITradeDocumentReaders readers,
    ICatalogService catalogs,
    IDocumentRepository documents,
    IChartOfAccountsProvider charts)
    : IDocumentPostValidator
{
    public string TypeCode => TradeCodes.CustomerPayment;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(CustomerPaymentPostValidator));

        var head = await readers.ReadCustomerPaymentHeadAsync(documentForUpdate.Id, ct);

        await TradeCatalogValidationGuards.EnsureCustomerAsync(head.CustomerId, "customer_id", catalogs, ct);

        if (head.Amount <= 0m)
            throw new NgbArgumentInvalidException("amount", "Amount must be greater than zero.");

        if (head.CashAccountId is { } cashAccountId)
            await TradeAccountingValidationGuards.EnsureCashAccountAsync(cashAccountId, "cash_account_id", charts, ct);

        if (head.SalesInvoiceId is { } salesInvoiceId)
        {
            await TradeDocumentReferenceValidationGuards.EnsurePostedSalesInvoiceAsync(
                salesInvoiceId,
                head.CustomerId,
                readers,
                documents,
                ct);
        }
    }
}
