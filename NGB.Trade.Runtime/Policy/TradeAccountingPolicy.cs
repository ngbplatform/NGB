namespace NGB.Trade.Runtime.Policy;

public sealed record TradeAccountingPolicy(
    Guid PolicyId,
    Guid CashAccountId,
    Guid AccountsReceivableAccountId,
    Guid InventoryAccountId,
    Guid AccountsPayableAccountId,
    Guid SalesRevenueAccountId,
    Guid CostOfGoodsSoldAccountId,
    Guid InventoryAdjustmentAccountId,
    Guid InventoryMovementsRegisterId,
    Guid ItemPricesRegisterId);
