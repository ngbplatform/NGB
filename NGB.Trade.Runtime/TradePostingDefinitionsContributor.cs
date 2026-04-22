using NGB.Definitions;
using NGB.Trade.Runtime.Documents.Validation;
using NGB.Trade.Runtime.Posting;

namespace NGB.Trade.Runtime;

/// <summary>
/// Runtime contribution for Trade document posting bindings.
/// </summary>
public sealed class TradePostingDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(
            TradeCodes.PurchaseReceipt,
            d => d
                .AddPostValidator<PurchaseReceiptPostValidator>()
                .PostingHandler<PurchaseReceiptPostingHandler>()
                .OperationalRegisterPostingHandler<PurchaseReceiptInventoryOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.SalesInvoice,
            d => d
                .AddPostValidator<SalesInvoicePostValidator>()
                .PostingHandler<SalesInvoicePostingHandler>()
                .OperationalRegisterPostingHandler<SalesInvoiceInventoryOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.CustomerPayment,
            d => d
                .AddPostValidator<CustomerPaymentPostValidator>()
                .PostingHandler<CustomerPaymentPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.VendorPayment,
            d => d
                .AddPostValidator<VendorPaymentPostValidator>()
                .PostingHandler<VendorPaymentPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.InventoryTransfer,
            d => d
                .AddPostValidator<InventoryTransferPostValidator>()
                .OperationalRegisterPostingHandler<InventoryTransferInventoryOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.InventoryAdjustment,
            d => d
                .AddPostValidator<InventoryAdjustmentPostValidator>()
                .PostingHandler<InventoryAdjustmentPostingHandler>()
                .OperationalRegisterPostingHandler<InventoryAdjustmentInventoryOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.CustomerReturn,
            d => d
                .AddPostValidator<CustomerReturnPostValidator>()
                .PostingHandler<CustomerReturnPostingHandler>()
                .OperationalRegisterPostingHandler<CustomerReturnInventoryOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.VendorReturn,
            d => d
                .AddPostValidator<VendorReturnPostValidator>()
                .PostingHandler<VendorReturnPostingHandler>()
                .OperationalRegisterPostingHandler<VendorReturnInventoryOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            TradeCodes.ItemPriceUpdate,
            d => d.ReferenceRegisterPostingHandler<ItemPriceUpdateReferenceRegisterPostingHandler>());
    }
}
