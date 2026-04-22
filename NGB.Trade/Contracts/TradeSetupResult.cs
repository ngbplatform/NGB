namespace NGB.Trade.Contracts;

/// <summary>
/// Outcome of running Trade default setup.
/// </summary>
public sealed record TradeSetupResult(
    Guid CashAccountId,
    Guid AccountsReceivableAccountId,
    Guid InventoryAccountId,
    Guid AccountsPayableAccountId,
    Guid SalesRevenueAccountId,
    Guid CostOfGoodsSoldAccountId,
    Guid InventoryAdjustmentAccountId,
    Guid InventoryMovementsOperationalRegisterId,
    Guid ItemPricesReferenceRegisterId,
    Guid AccountingPolicyCatalogId,
    bool CreatedCashAccount,
    bool CreatedAccountsReceivableAccount,
    bool CreatedInventoryAccount,
    bool CreatedAccountsPayableAccount,
    bool CreatedSalesRevenueAccount,
    bool CreatedCostOfGoodsSoldAccount,
    bool CreatedInventoryAdjustmentAccount,
    bool CreatedInventoryMovementsOperationalRegister,
    bool CreatedItemPricesReferenceRegister,
    bool CreatedAccountingPolicy);
