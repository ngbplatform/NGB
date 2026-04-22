using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.Trade.Documents;

namespace NGB.Trade.PostgreSql.Documents;

public sealed class TradeDocumentReaders(IUnitOfWork uow) : ITradeDocumentReaders
{
    public async Task<TradePurchaseReceiptHead> ReadPurchaseReceiptHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    vendor_id AS VendorId,
    warehouse_id AS WarehouseId,
    notes AS Notes,
    amount AS Amount
FROM doc_trd_purchase_receipt
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradePurchaseReceiptHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradePurchaseReceiptLine>> ReadPurchaseReceiptLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    item_id AS ItemId,
    quantity AS Quantity,
    unit_cost AS UnitCost,
    line_amount AS LineAmount
FROM doc_trd_purchase_receipt__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<TradePurchaseReceiptLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<TradeSalesInvoiceHead> ReadSalesInvoiceHeadAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    customer_id AS CustomerId,
    warehouse_id AS WarehouseId,
    price_type_id AS PriceTypeId,
    notes AS Notes,
    amount AS Amount
FROM doc_trd_sales_invoice
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeSalesInvoiceHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradeSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    item_id AS ItemId,
    quantity AS Quantity,
    unit_price AS UnitPrice,
    unit_cost AS UnitCost,
    line_amount AS LineAmount
FROM doc_trd_sales_invoice__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<TradeSalesInvoiceLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<TradeInventoryTransferHead> ReadInventoryTransferHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    from_warehouse_id AS FromWarehouseId,
    to_warehouse_id AS ToWarehouseId,
    notes AS Notes
FROM doc_trd_inventory_transfer
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeInventoryTransferHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradeInventoryTransferLine>> ReadInventoryTransferLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    item_id AS ItemId,
    quantity AS Quantity
FROM doc_trd_inventory_transfer__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<TradeInventoryTransferLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<TradeInventoryAdjustmentHead> ReadInventoryAdjustmentHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    warehouse_id AS WarehouseId,
    reason_id AS ReasonId,
    notes AS Notes,
    amount AS Amount
FROM doc_trd_inventory_adjustment
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeInventoryAdjustmentHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradeInventoryAdjustmentLine>> ReadInventoryAdjustmentLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    item_id AS ItemId,
    quantity_delta AS QuantityDelta,
    unit_cost AS UnitCost,
    line_amount AS LineAmount
FROM doc_trd_inventory_adjustment__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<TradeInventoryAdjustmentLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<TradeCustomerReturnHead> ReadCustomerReturnHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    customer_id AS CustomerId,
    warehouse_id AS WarehouseId,
    sales_invoice_id AS SalesInvoiceId,
    notes AS Notes,
    amount AS Amount
FROM doc_trd_customer_return
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeCustomerReturnHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradeCustomerReturnLine>> ReadCustomerReturnLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    item_id AS ItemId,
    quantity AS Quantity,
    unit_price AS UnitPrice,
    unit_cost AS UnitCost,
    line_amount AS LineAmount
FROM doc_trd_customer_return__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<TradeCustomerReturnLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<TradeVendorReturnHead> ReadVendorReturnHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    vendor_id AS VendorId,
    warehouse_id AS WarehouseId,
    purchase_receipt_id AS PurchaseReceiptId,
    notes AS Notes,
    amount AS Amount
FROM doc_trd_vendor_return
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeVendorReturnHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradeVendorReturnLine>> ReadVendorReturnLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    item_id AS ItemId,
    quantity AS Quantity,
    unit_cost AS UnitCost,
    line_amount AS LineAmount
FROM doc_trd_vendor_return__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<TradeVendorReturnLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<TradeCustomerPaymentHead> ReadCustomerPaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    customer_id AS CustomerId,
    cash_account_id AS CashAccountId,
    sales_invoice_id AS SalesInvoiceId,
    amount AS Amount,
    notes AS Notes
FROM doc_trd_customer_payment
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeCustomerPaymentHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<TradeVendorPaymentHead> ReadVendorPaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    vendor_id AS VendorId,
    cash_account_id AS CashAccountId,
    purchase_receipt_id AS PurchaseReceiptId,
    amount AS Amount,
    notes AS Notes
FROM doc_trd_vendor_payment
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeVendorPaymentHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<TradeItemPriceUpdateHead> ReadItemPriceUpdateHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    effective_date AS EffectiveDate,
    notes AS Notes
FROM doc_trd_item_price_update
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<TradeItemPriceUpdateHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradeItemPriceUpdateLine>> ReadItemPriceUpdateLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    item_id AS ItemId,
    price_type_id AS PriceTypeId,
    currency AS Currency,
    unit_price AS UnitPrice
FROM doc_trd_item_price_update__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<TradeItemPriceUpdateLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }
}
