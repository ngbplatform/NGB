namespace NGB.Trade.Documents;

public sealed record TradeVendorPaymentHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid VendorId,
    Guid? CashAccountId,
    Guid? PurchaseReceiptId,
    decimal Amount,
    string? Notes);
