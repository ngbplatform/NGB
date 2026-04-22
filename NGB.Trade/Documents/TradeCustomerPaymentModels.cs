namespace NGB.Trade.Documents;

public sealed record TradeCustomerPaymentHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid CustomerId,
    Guid? CashAccountId,
    Guid? SalesInvoiceId,
    decimal Amount,
    string? Notes);
