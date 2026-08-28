using Dapper;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Trade.Reporting;

namespace NGB.Trade.PostgreSql.Reporting;

public sealed class PostgresTradeAnalyticsReader(IUnitOfWork uow) : ITradeAnalyticsReader
{
    public async Task<TradeDashboardAnalyticsSnapshot> GetDashboardOverviewAsync(
        DateOnly fromInclusive,
        DateOnly asOfInclusive,
        int topItemLimit,
        int recentDocumentLimit,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topItemLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recentDocumentLimit, 1);

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH sales AS (
    SELECT
        line.item_id,
        SUM(line.quantity) AS sold_quantity,
        SUM(line.line_amount) AS gross_sales,
        SUM(line.quantity * line.unit_cost) AS sold_cogs
    FROM doc_trd_sales_invoice head
    JOIN documents document
      ON document.id = head.document_id
     AND document.status = @posted_status
    JOIN doc_trd_sales_invoice__lines line ON line.document_id = head.document_id
    WHERE head.document_date_utc >= @from_utc
      AND head.document_date_utc <= @as_of_utc
    GROUP BY line.item_id
),
returns AS (
    SELECT
        line.item_id,
        SUM(line.quantity) AS returned_quantity,
        SUM(line.line_amount) AS returned_amount,
        SUM(line.quantity * line.unit_cost) AS returned_cogs
    FROM doc_trd_customer_return head
    JOIN documents document
      ON document.id = head.document_id
     AND document.status = @posted_status
    JOIN doc_trd_customer_return__lines line ON line.document_id = head.document_id
    WHERE head.document_date_utc >= @from_utc
      AND head.document_date_utc <= @as_of_utc
    GROUP BY line.item_id
),
keys AS (
    SELECT item_id FROM sales
    UNION
    SELECT item_id FROM returns
)
SELECT
    keys.item_id AS ItemId,
    COALESCE(item.display, keys.item_id::text) AS ItemDisplay,
    COALESCE(sales.sold_quantity, 0) AS SoldQuantity,
    COALESCE(sales.gross_sales, 0) AS GrossSales,
    COALESCE(returns.returned_quantity, 0) AS ReturnedQuantity,
    COALESCE(returns.returned_amount, 0) AS ReturnedAmount,
    COALESCE(sales.gross_sales, 0) - COALESCE(returns.returned_amount, 0) AS NetSales,
    COALESCE(sales.sold_cogs, 0) - COALESCE(returns.returned_cogs, 0) AS NetCogs,
    COUNT(*) OVER()::integer AS TotalCount,
    SUM(COALESCE(sales.sold_quantity, 0)) OVER() AS TotalSoldQuantity,
    SUM(COALESCE(sales.gross_sales, 0)) OVER() AS TotalGrossSales,
    SUM(COALESCE(returns.returned_quantity, 0)) OVER() AS TotalReturnedQuantity,
    SUM(COALESCE(returns.returned_amount, 0)) OVER() AS TotalReturnedAmount,
    SUM(COALESCE(sales.gross_sales, 0) - COALESCE(returns.returned_amount, 0)) OVER() AS TotalNetSales,
    SUM(COALESCE(sales.sold_cogs, 0) - COALESCE(returns.returned_cogs, 0)) OVER() AS TotalNetCogs
FROM keys
LEFT JOIN cat_trd_item item ON item.catalog_id = keys.item_id
LEFT JOIN sales ON sales.item_id = keys.item_id
LEFT JOIN returns ON returns.item_id = keys.item_id
WHERE COALESCE(sales.sold_quantity, 0) <> 0
   OR COALESCE(returns.returned_quantity, 0) <> 0
ORDER BY NetSales DESC, ItemDisplay
LIMIT @top_item_limit;

WITH purchases AS (
    SELECT COALESCE(SUM(line.line_amount), 0) AS amount
    FROM doc_trd_purchase_receipt head
    JOIN documents document
      ON document.id = head.document_id
     AND document.status = @posted_status
    JOIN doc_trd_purchase_receipt__lines line ON line.document_id = head.document_id
    WHERE head.document_date_utc >= @from_utc
      AND head.document_date_utc <= @as_of_utc
),
vendor_returns AS (
    SELECT COALESCE(SUM(line.line_amount), 0) AS amount
    FROM doc_trd_vendor_return head
    JOIN documents document
      ON document.id = head.document_id
     AND document.status = @posted_status
    JOIN doc_trd_vendor_return__lines line ON line.document_id = head.document_id
    WHERE head.document_date_utc >= @from_utc
      AND head.document_date_utc <= @as_of_utc
)
SELECT purchases.amount - vendor_returns.amount AS NetPurchases
FROM purchases CROSS JOIN vendor_returns;

SELECT
    document.id AS DocumentId,
    document.type_code AS DocumentTypeCode,
    CASE document.type_code
        WHEN @purchase_receipt_type THEN 'Purchase Receipt'
        WHEN @sales_invoice_type THEN 'Sales Invoice'
        WHEN @customer_payment_type THEN 'Customer Payment'
        WHEN @vendor_payment_type THEN 'Vendor Payment'
        WHEN @customer_return_type THEN 'Customer Return'
        WHEN @vendor_return_type THEN 'Vendor Return'
    END AS DocumentTypeDisplay,
    COALESCE(
        purchase.display,
        sale.display,
        customer_payment.display,
        vendor_payment.display,
        customer_return.display,
        vendor_return.display,
        document.number,
        document.id::text) AS DocumentDisplay,
    COALESCE(
        purchase.document_date_utc,
        sale.document_date_utc,
        customer_payment.document_date_utc,
        vendor_payment.document_date_utc,
        customer_return.document_date_utc,
        vendor_return.document_date_utc) AS DocumentDateUtc,
    document.updated_at_utc AS UpdatedAtUtc,
    CASE document.status
        WHEN 1 THEN 'Draft'
        WHEN 2 THEN 'Posted'
        WHEN 3 THEN 'Marked for deletion'
        ELSE document.status::text
    END AS StatusDisplay,
    partner.display AS PartnerDisplay,
    COALESCE(
        purchase.amount,
        sale.amount,
        customer_payment.amount,
        vendor_payment.amount,
        customer_return.amount,
        vendor_return.amount) AS Amount
FROM documents document
LEFT JOIN doc_trd_purchase_receipt purchase
  ON document.type_code = @purchase_receipt_type AND purchase.document_id = document.id
LEFT JOIN doc_trd_sales_invoice sale
  ON document.type_code = @sales_invoice_type AND sale.document_id = document.id
LEFT JOIN doc_trd_customer_payment customer_payment
  ON document.type_code = @customer_payment_type AND customer_payment.document_id = document.id
LEFT JOIN doc_trd_vendor_payment vendor_payment
  ON document.type_code = @vendor_payment_type AND vendor_payment.document_id = document.id
LEFT JOIN doc_trd_customer_return customer_return
  ON document.type_code = @customer_return_type AND customer_return.document_id = document.id
LEFT JOIN doc_trd_vendor_return vendor_return
  ON document.type_code = @vendor_return_type AND vendor_return.document_id = document.id
LEFT JOIN cat_trd_party partner ON partner.catalog_id = COALESCE(
    purchase.vendor_id,
    sale.customer_id,
    customer_payment.customer_id,
    vendor_payment.vendor_id,
    customer_return.customer_id,
    vendor_return.vendor_id)
WHERE document.type_code = ANY(@recent_document_types)
  AND document.status <> @deleted_status
  AND COALESCE(
        purchase.document_date_utc,
        sale.document_date_utc,
        customer_payment.document_date_utc,
        vendor_payment.document_date_utc,
        customer_return.document_date_utc,
        vendor_return.document_date_utc) <= @as_of_utc
ORDER BY document.updated_at_utc DESC, DocumentDateUtc DESC, DocumentDisplay
LIMIT @recent_document_limit;
""";

        var command = new CommandDefinition(
            sql,
            new
            {
                posted_status = (short)DocumentStatus.Posted,
                deleted_status = (short)DocumentStatus.MarkedForDeletion,
                from_utc = fromInclusive,
                as_of_utc = asOfInclusive,
                top_item_limit = topItemLimit,
                recent_document_limit = recentDocumentLimit,
                purchase_receipt_type = TradeCodes.PurchaseReceipt,
                sales_invoice_type = TradeCodes.SalesInvoice,
                customer_payment_type = TradeCodes.CustomerPayment,
                vendor_payment_type = TradeCodes.VendorPayment,
                customer_return_type = TradeCodes.CustomerReturn,
                vendor_return_type = TradeCodes.VendorReturn,
                recent_document_types = new[]
                {
                    TradeCodes.PurchaseReceipt,
                    TradeCodes.SalesInvoice,
                    TradeCodes.CustomerPayment,
                    TradeCodes.VendorPayment,
                    TradeCodes.CustomerReturn,
                    TradeCodes.VendorReturn
                }
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await using var grid = await uow.Connection.QueryMultipleAsync(command);
        var salesRows = (await grid.ReadAsync<SalesByItemPageSqlRow>()).AsList();
        var purchases = await grid.ReadSingleAsync<DashboardPurchasesSqlRow>();
        var recentDocuments = (await grid.ReadAsync<RecentTradeDocumentSummaryRow>()).AsList();
        var first = salesRows.FirstOrDefault();
        var salesPage = new TradeAnalyticsPage<SalesByItemSummaryRow, SalesByItemTotals>(
            salesRows.Select(static row => new SalesByItemSummaryRow(
                row.ItemId,
                row.ItemDisplay,
                row.SoldQuantity,
                row.GrossSales,
                row.ReturnedQuantity,
                row.ReturnedAmount,
                row.NetSales,
                row.NetCogs)).ToArray(),
            first?.TotalCount ?? 0,
            first is null
                ? new SalesByItemTotals(0m, 0m, 0m, 0m, 0m, 0m)
                : new SalesByItemTotals(
                    first.TotalSoldQuantity,
                    first.TotalGrossSales,
                    first.TotalReturnedQuantity,
                    first.TotalReturnedAmount,
                    first.TotalNetSales,
                    first.TotalNetCogs));

        return new TradeDashboardAnalyticsSnapshot(salesPage, purchases.NetPurchases, recentDocuments);
    }

    public async Task<TradeAnalyticsPage<SalesByItemSummaryRow, SalesByItemTotals>> GetSalesByItemPageAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? warehouseIds,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH sales AS (
    SELECT
        l.item_id AS item_id,
        SUM(l.quantity) AS sold_quantity,
        SUM(l.line_amount) AS gross_sales,
        SUM(l.quantity * l.unit_cost) AS sold_cogs
    FROM doc_trd_sales_invoice h
    INNER JOIN documents d
        ON d.id = h.document_id
    INNER JOIN doc_trd_sales_invoice__lines l
        ON l.document_id = h.document_id
    WHERE d.type_code = @sales_invoice_type
      AND d.status = @posted_status
      AND h.document_date_utc >= @from_utc
      AND h.document_date_utc <= @to_utc
      AND (@has_item_filter = FALSE OR l.item_id = ANY(@item_ids))
      AND (@has_customer_filter = FALSE OR h.customer_id = ANY(@customer_ids))
      AND (@has_warehouse_filter = FALSE OR h.warehouse_id = ANY(@warehouse_ids))
    GROUP BY l.item_id
),
returns AS (
    SELECT
        l.item_id AS item_id,
        SUM(l.quantity) AS returned_quantity,
        SUM(l.line_amount) AS returned_amount,
        SUM(l.quantity * l.unit_cost) AS returned_cogs
    FROM doc_trd_customer_return h
    INNER JOIN documents d
        ON d.id = h.document_id
    INNER JOIN doc_trd_customer_return__lines l
        ON l.document_id = h.document_id
    WHERE d.type_code = @customer_return_type
      AND d.status = @posted_status
      AND h.document_date_utc >= @from_utc
      AND h.document_date_utc <= @to_utc
      AND (@has_item_filter = FALSE OR l.item_id = ANY(@item_ids))
      AND (@has_customer_filter = FALSE OR h.customer_id = ANY(@customer_ids))
      AND (@has_warehouse_filter = FALSE OR h.warehouse_id = ANY(@warehouse_ids))
    GROUP BY l.item_id
),
keys AS (
    SELECT item_id FROM sales
    UNION
    SELECT item_id FROM returns
)
SELECT
    k.item_id AS ItemId,
    COALESCE(i.display, k.item_id::text) AS ItemDisplay,
    COALESCE(s.sold_quantity, 0) AS SoldQuantity,
    COALESCE(s.gross_sales, 0) AS GrossSales,
    COALESCE(r.returned_quantity, 0) AS ReturnedQuantity,
    COALESCE(r.returned_amount, 0) AS ReturnedAmount,
    COALESCE(s.gross_sales, 0) - COALESCE(r.returned_amount, 0) AS NetSales,
    COALESCE(s.sold_cogs, 0) - COALESCE(r.returned_cogs, 0) AS NetCogs,
    COUNT(*) OVER()::integer AS TotalCount,
    SUM(COALESCE(s.sold_quantity, 0)) OVER() AS TotalSoldQuantity,
    SUM(COALESCE(s.gross_sales, 0)) OVER() AS TotalGrossSales,
    SUM(COALESCE(r.returned_quantity, 0)) OVER() AS TotalReturnedQuantity,
    SUM(COALESCE(r.returned_amount, 0)) OVER() AS TotalReturnedAmount,
    SUM(COALESCE(s.gross_sales, 0) - COALESCE(r.returned_amount, 0)) OVER() AS TotalNetSales,
    SUM(COALESCE(s.sold_cogs, 0) - COALESCE(r.returned_cogs, 0)) OVER() AS TotalNetCogs
FROM keys k
LEFT JOIN cat_trd_item i
    ON i.catalog_id = k.item_id
LEFT JOIN sales s
    ON s.item_id = k.item_id
LEFT JOIN returns r
    ON r.item_id = k.item_id
WHERE COALESCE(s.sold_quantity, 0) <> 0
   OR COALESCE(r.returned_quantity, 0) <> 0
ORDER BY
    COALESCE(s.gross_sales, 0) - COALESCE(r.returned_amount, 0) DESC,
    COALESCE(i.display, k.item_id::text) ASC
OFFSET @offset
LIMIT @limit;
""";

        var itemIdArray = NormalizeIds(itemIds);
        var customerIdArray = NormalizeIds(customerIds);
        var warehouseIdArray = NormalizeIds(warehouseIds);

        var rows = (await uow.Connection.QueryAsync<SalesByItemPageSqlRow>(new CommandDefinition(
            sql,
            new
            {
                sales_invoice_type = TradeCodes.SalesInvoice,
                customer_return_type = TradeCodes.CustomerReturn,
                posted_status = (short)DocumentStatus.Posted,
                from_utc = fromInclusive,
                to_utc = toInclusive,
                has_item_filter = itemIdArray.Length > 0,
                has_customer_filter = customerIdArray.Length > 0,
                has_warehouse_filter = warehouseIdArray.Length > 0,
                item_ids = itemIdArray,
                customer_ids = customerIdArray,
                warehouse_ids = warehouseIdArray,
                offset = PagingLimits.BoundOffset(offset),
                limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var first = rows.FirstOrDefault();

        return new TradeAnalyticsPage<SalesByItemSummaryRow, SalesByItemTotals>(
            rows.Select(static row => new SalesByItemSummaryRow(
                row.ItemId,
                row.ItemDisplay,
                row.SoldQuantity,
                row.GrossSales,
                row.ReturnedQuantity,
                row.ReturnedAmount,
                row.NetSales,
                row.NetCogs)).ToArray(),
            first?.TotalCount ?? 0,
            first is null
                ? new SalesByItemTotals(0m, 0m, 0m, 0m, 0m, 0m)
                : new SalesByItemTotals(
                    first.TotalSoldQuantity,
                    first.TotalGrossSales,
                    first.TotalReturnedQuantity,
                    first.TotalReturnedAmount,
                    first.TotalNetSales,
                    first.TotalNetCogs));
    }

    public async Task<IReadOnlyList<SalesByItemSummaryRow>> GetSalesByItemAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? warehouseIds,
        CancellationToken ct = default)
        => (await GetSalesByItemPageAsync(
            fromInclusive,
            toInclusive,
            itemIds,
            customerIds,
            warehouseIds,
            0,
            int.MaxValue,
            ct)).Rows;

    public async Task<TradeAnalyticsPage<SalesByCustomerSummaryRow, SalesByCustomerTotals>> GetSalesByCustomerPageAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH sales AS (
    SELECT
        h.customer_id AS customer_id,
        COUNT(DISTINCT h.document_id)::integer AS sales_document_count,
        SUM(l.line_amount) AS gross_sales,
        SUM(l.quantity * l.unit_cost) AS sold_cogs
    FROM doc_trd_sales_invoice h
    INNER JOIN documents d
        ON d.id = h.document_id
    INNER JOIN doc_trd_sales_invoice__lines l
        ON l.document_id = h.document_id
    WHERE d.type_code = @sales_invoice_type
      AND d.status = @posted_status
      AND h.document_date_utc >= @from_utc
      AND h.document_date_utc <= @to_utc
      AND (@has_customer_filter = FALSE OR h.customer_id = ANY(@customer_ids))
      AND (@has_item_filter = FALSE OR l.item_id = ANY(@item_ids))
      AND (@has_warehouse_filter = FALSE OR h.warehouse_id = ANY(@warehouse_ids))
    GROUP BY h.customer_id
),
returns AS (
    SELECT
        h.customer_id AS customer_id,
        COUNT(DISTINCT h.document_id)::integer AS return_document_count,
        SUM(l.line_amount) AS returned_amount,
        SUM(l.quantity * l.unit_cost) AS returned_cogs
    FROM doc_trd_customer_return h
    INNER JOIN documents d
        ON d.id = h.document_id
    INNER JOIN doc_trd_customer_return__lines l
        ON l.document_id = h.document_id
    WHERE d.type_code = @customer_return_type
      AND d.status = @posted_status
      AND h.document_date_utc >= @from_utc
      AND h.document_date_utc <= @to_utc
      AND (@has_customer_filter = FALSE OR h.customer_id = ANY(@customer_ids))
      AND (@has_item_filter = FALSE OR l.item_id = ANY(@item_ids))
      AND (@has_warehouse_filter = FALSE OR h.warehouse_id = ANY(@warehouse_ids))
    GROUP BY h.customer_id
),
keys AS (
    SELECT customer_id FROM sales
    UNION
    SELECT customer_id FROM returns
)
SELECT
    k.customer_id AS CustomerId,
    COALESCE(p.display, k.customer_id::text) AS CustomerDisplay,
    COALESCE(s.sales_document_count, 0) AS SalesDocumentCount,
    COALESCE(r.return_document_count, 0) AS ReturnDocumentCount,
    COALESCE(s.gross_sales, 0) AS GrossSales,
    COALESCE(r.returned_amount, 0) AS ReturnedAmount,
    COALESCE(s.gross_sales, 0) - COALESCE(r.returned_amount, 0) AS NetSales,
    COALESCE(s.sold_cogs, 0) - COALESCE(r.returned_cogs, 0) AS NetCogs,
    COUNT(*) OVER()::integer AS TotalCount,
    SUM(COALESCE(s.sales_document_count, 0)) OVER()::integer AS TotalSalesDocumentCount,
    SUM(COALESCE(r.return_document_count, 0)) OVER()::integer AS TotalReturnDocumentCount,
    SUM(COALESCE(s.gross_sales, 0)) OVER() AS TotalGrossSales,
    SUM(COALESCE(r.returned_amount, 0)) OVER() AS TotalReturnedAmount,
    SUM(COALESCE(s.gross_sales, 0) - COALESCE(r.returned_amount, 0)) OVER() AS TotalNetSales,
    SUM(COALESCE(s.sold_cogs, 0) - COALESCE(r.returned_cogs, 0)) OVER() AS TotalNetCogs
FROM keys k
LEFT JOIN cat_trd_party p
    ON p.catalog_id = k.customer_id
LEFT JOIN sales s
    ON s.customer_id = k.customer_id
LEFT JOIN returns r
    ON r.customer_id = k.customer_id
WHERE COALESCE(s.sales_document_count, 0) <> 0
   OR COALESCE(r.return_document_count, 0) <> 0
ORDER BY
    COALESCE(s.gross_sales, 0) - COALESCE(r.returned_amount, 0) DESC,
    COALESCE(p.display, k.customer_id::text) ASC
OFFSET @offset
LIMIT @limit;
""";

        var customerIdArray = NormalizeIds(customerIds);
        var itemIdArray = NormalizeIds(itemIds);
        var warehouseIdArray = NormalizeIds(warehouseIds);

        var rows = (await uow.Connection.QueryAsync<SalesByCustomerPageSqlRow>(new CommandDefinition(
            sql,
            new
            {
                sales_invoice_type = TradeCodes.SalesInvoice,
                customer_return_type = TradeCodes.CustomerReturn,
                posted_status = (short)DocumentStatus.Posted,
                from_utc = fromInclusive,
                to_utc = toInclusive,
                has_customer_filter = customerIdArray.Length > 0,
                has_item_filter = itemIdArray.Length > 0,
                has_warehouse_filter = warehouseIdArray.Length > 0,
                customer_ids = customerIdArray,
                item_ids = itemIdArray,
                warehouse_ids = warehouseIdArray,
                offset = PagingLimits.BoundOffset(offset),
                limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var first = rows.FirstOrDefault();

        return new TradeAnalyticsPage<SalesByCustomerSummaryRow, SalesByCustomerTotals>(
            rows.Select(static row => new SalesByCustomerSummaryRow(
                row.CustomerId,
                row.CustomerDisplay,
                row.SalesDocumentCount,
                row.ReturnDocumentCount,
                row.GrossSales,
                row.ReturnedAmount,
                row.NetSales,
                row.NetCogs)).ToArray(),
            first?.TotalCount ?? 0,
            first is null
                ? new SalesByCustomerTotals(0, 0, 0m, 0m, 0m, 0m)
                : new SalesByCustomerTotals(
                    first.TotalSalesDocumentCount,
                    first.TotalReturnDocumentCount,
                    first.TotalGrossSales,
                    first.TotalReturnedAmount,
                    first.TotalNetSales,
                    first.TotalNetCogs));
    }

    public async Task<IReadOnlyList<SalesByCustomerSummaryRow>> GetSalesByCustomerAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        CancellationToken ct = default)
        => (await GetSalesByCustomerPageAsync(
            fromInclusive,
            toInclusive,
            customerIds,
            itemIds,
            warehouseIds,
            0,
            int.MaxValue,
            ct)).Rows;

    public async Task<TradeAnalyticsPage<PurchasesByVendorSummaryRow, PurchasesByVendorTotals>> GetPurchasesByVendorPageAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? vendorIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH purchases AS (
    SELECT
        h.vendor_id AS vendor_id,
        COUNT(DISTINCT h.document_id)::integer AS purchase_document_count,
        SUM(l.line_amount) AS gross_purchases
    FROM doc_trd_purchase_receipt h
    INNER JOIN documents d
        ON d.id = h.document_id
    INNER JOIN doc_trd_purchase_receipt__lines l
        ON l.document_id = h.document_id
    WHERE d.type_code = @purchase_receipt_type
      AND d.status = @posted_status
      AND h.document_date_utc >= @from_utc
      AND h.document_date_utc <= @to_utc
      AND (@has_vendor_filter = FALSE OR h.vendor_id = ANY(@vendor_ids))
      AND (@has_item_filter = FALSE OR l.item_id = ANY(@item_ids))
      AND (@has_warehouse_filter = FALSE OR h.warehouse_id = ANY(@warehouse_ids))
    GROUP BY h.vendor_id
),
returns AS (
    SELECT
        h.vendor_id AS vendor_id,
        COUNT(DISTINCT h.document_id)::integer AS return_document_count,
        SUM(l.line_amount) AS returned_amount
    FROM doc_trd_vendor_return h
    INNER JOIN documents d
        ON d.id = h.document_id
    INNER JOIN doc_trd_vendor_return__lines l
        ON l.document_id = h.document_id
    WHERE d.type_code = @vendor_return_type
      AND d.status = @posted_status
      AND h.document_date_utc >= @from_utc
      AND h.document_date_utc <= @to_utc
      AND (@has_vendor_filter = FALSE OR h.vendor_id = ANY(@vendor_ids))
      AND (@has_item_filter = FALSE OR l.item_id = ANY(@item_ids))
      AND (@has_warehouse_filter = FALSE OR h.warehouse_id = ANY(@warehouse_ids))
    GROUP BY h.vendor_id
),
keys AS (
    SELECT vendor_id FROM purchases
    UNION
    SELECT vendor_id FROM returns
)
SELECT
    k.vendor_id AS VendorId,
    COALESCE(p.display, k.vendor_id::text) AS VendorDisplay,
    COALESCE(pr.purchase_document_count, 0) AS PurchaseDocumentCount,
    COALESCE(vr.return_document_count, 0) AS ReturnDocumentCount,
    COALESCE(pr.gross_purchases, 0) AS GrossPurchases,
    COALESCE(vr.returned_amount, 0) AS ReturnedAmount,
    COALESCE(pr.gross_purchases, 0) - COALESCE(vr.returned_amount, 0) AS NetPurchases
    ,COUNT(*) OVER()::integer AS TotalCount
    ,SUM(COALESCE(pr.purchase_document_count, 0)) OVER()::integer AS TotalPurchaseDocumentCount
    ,SUM(COALESCE(vr.return_document_count, 0)) OVER()::integer AS TotalReturnDocumentCount
    ,SUM(COALESCE(pr.gross_purchases, 0)) OVER() AS TotalGrossPurchases
    ,SUM(COALESCE(vr.returned_amount, 0)) OVER() AS TotalReturnedAmount
    ,SUM(COALESCE(pr.gross_purchases, 0) - COALESCE(vr.returned_amount, 0)) OVER() AS TotalNetPurchases
FROM keys k
LEFT JOIN cat_trd_party p
    ON p.catalog_id = k.vendor_id
LEFT JOIN purchases pr
    ON pr.vendor_id = k.vendor_id
LEFT JOIN returns vr
    ON vr.vendor_id = k.vendor_id
WHERE COALESCE(pr.purchase_document_count, 0) <> 0
   OR COALESCE(vr.return_document_count, 0) <> 0
ORDER BY
    COALESCE(pr.gross_purchases, 0) - COALESCE(vr.returned_amount, 0) DESC,
    COALESCE(p.display, k.vendor_id::text) ASC
OFFSET @offset
LIMIT @limit;
""";

        var vendorIdArray = NormalizeIds(vendorIds);
        var itemIdArray = NormalizeIds(itemIds);
        var warehouseIdArray = NormalizeIds(warehouseIds);

        var rows = (await uow.Connection.QueryAsync<PurchasesByVendorPageSqlRow>(new CommandDefinition(
            sql,
            new
            {
                purchase_receipt_type = TradeCodes.PurchaseReceipt,
                vendor_return_type = TradeCodes.VendorReturn,
                posted_status = (short)DocumentStatus.Posted,
                from_utc = fromInclusive,
                to_utc = toInclusive,
                has_vendor_filter = vendorIdArray.Length > 0,
                has_item_filter = itemIdArray.Length > 0,
                has_warehouse_filter = warehouseIdArray.Length > 0,
                vendor_ids = vendorIdArray,
                item_ids = itemIdArray,
                warehouse_ids = warehouseIdArray,
                offset = PagingLimits.BoundOffset(offset),
                limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var first = rows.FirstOrDefault();

        return new TradeAnalyticsPage<PurchasesByVendorSummaryRow, PurchasesByVendorTotals>(
            rows.Select(static row => new PurchasesByVendorSummaryRow(
                row.VendorId,
                row.VendorDisplay,
                row.PurchaseDocumentCount,
                row.ReturnDocumentCount,
                row.GrossPurchases,
                row.ReturnedAmount,
                row.NetPurchases)).ToArray(),
            first?.TotalCount ?? 0,
            first is null
                ? new PurchasesByVendorTotals(0, 0, 0m, 0m, 0m)
                : new PurchasesByVendorTotals(
                    first.TotalPurchaseDocumentCount,
                    first.TotalReturnDocumentCount,
                    first.TotalGrossPurchases,
                    first.TotalReturnedAmount,
                    first.TotalNetPurchases));
    }

    public async Task<IReadOnlyList<PurchasesByVendorSummaryRow>> GetPurchasesByVendorAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? vendorIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        CancellationToken ct = default)
        => (await GetPurchasesByVendorPageAsync(
            fromInclusive,
            toInclusive,
            vendorIds,
            itemIds,
            warehouseIds,
            0,
            int.MaxValue,
            ct)).Rows;

    public async Task<IReadOnlyList<RecentTradeDocumentSummaryRow>> GetRecentDocumentsAsync(
        DateOnly asOf,
        int limit,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH recent_candidates AS (
    SELECT
        d.id AS DocumentId,
        d.type_code AS DocumentTypeCode,
        'Purchase Receipt' AS DocumentTypeDisplay,
        COALESCE(h.display, d.number, d.id::text) AS DocumentDisplay,
        h.document_date_utc AS DocumentDateUtc,
        d.updated_at_utc AS UpdatedAtUtc,
        CASE d.status
            WHEN 1 THEN 'Draft'
            WHEN 2 THEN 'Posted'
            WHEN 3 THEN 'Marked for deletion'
            ELSE d.status::text
        END AS StatusDisplay,
        partner.display AS PartnerDisplay,
        h.amount AS HeaderAmount
    FROM documents d
    INNER JOIN doc_trd_purchase_receipt h
        ON h.document_id = d.id
    LEFT JOIN cat_trd_party partner
        ON partner.catalog_id = h.vendor_id
    WHERE d.type_code = @purchase_receipt_type
      AND d.status <> @deleted_status
      AND h.document_date_utc <= @as_of_utc

    UNION ALL

    SELECT
        d.id AS DocumentId,
        d.type_code AS DocumentTypeCode,
        'Sales Invoice' AS DocumentTypeDisplay,
        COALESCE(h.display, d.number, d.id::text) AS DocumentDisplay,
        h.document_date_utc AS DocumentDateUtc,
        d.updated_at_utc AS UpdatedAtUtc,
        CASE d.status
            WHEN 1 THEN 'Draft'
            WHEN 2 THEN 'Posted'
            WHEN 3 THEN 'Marked for deletion'
            ELSE d.status::text
        END AS StatusDisplay,
        partner.display AS PartnerDisplay,
        h.amount AS HeaderAmount
    FROM documents d
    INNER JOIN doc_trd_sales_invoice h
        ON h.document_id = d.id
    LEFT JOIN cat_trd_party partner
        ON partner.catalog_id = h.customer_id
    WHERE d.type_code = @sales_invoice_type
      AND d.status <> @deleted_status
      AND h.document_date_utc <= @as_of_utc

    UNION ALL

    SELECT
        d.id AS DocumentId,
        d.type_code AS DocumentTypeCode,
        'Customer Payment' AS DocumentTypeDisplay,
        COALESCE(h.display, d.number, d.id::text) AS DocumentDisplay,
        h.document_date_utc AS DocumentDateUtc,
        d.updated_at_utc AS UpdatedAtUtc,
        CASE d.status
            WHEN 1 THEN 'Draft'
            WHEN 2 THEN 'Posted'
            WHEN 3 THEN 'Marked for deletion'
            ELSE d.status::text
        END AS StatusDisplay,
        partner.display AS PartnerDisplay,
        h.amount AS HeaderAmount
    FROM documents d
    INNER JOIN doc_trd_customer_payment h
        ON h.document_id = d.id
    LEFT JOIN cat_trd_party partner
        ON partner.catalog_id = h.customer_id
    WHERE d.type_code = @customer_payment_type
      AND d.status <> @deleted_status
      AND h.document_date_utc <= @as_of_utc

    UNION ALL

    SELECT
        d.id AS DocumentId,
        d.type_code AS DocumentTypeCode,
        'Vendor Payment' AS DocumentTypeDisplay,
        COALESCE(h.display, d.number, d.id::text) AS DocumentDisplay,
        h.document_date_utc AS DocumentDateUtc,
        d.updated_at_utc AS UpdatedAtUtc,
        CASE d.status
            WHEN 1 THEN 'Draft'
            WHEN 2 THEN 'Posted'
            WHEN 3 THEN 'Marked for deletion'
            ELSE d.status::text
        END AS StatusDisplay,
        partner.display AS PartnerDisplay,
        h.amount AS HeaderAmount
    FROM documents d
    INNER JOIN doc_trd_vendor_payment h
        ON h.document_id = d.id
    LEFT JOIN cat_trd_party partner
        ON partner.catalog_id = h.vendor_id
    WHERE d.type_code = @vendor_payment_type
      AND d.status <> @deleted_status
      AND h.document_date_utc <= @as_of_utc

    UNION ALL

    SELECT
        d.id AS DocumentId,
        d.type_code AS DocumentTypeCode,
        'Customer Return' AS DocumentTypeDisplay,
        COALESCE(h.display, d.number, d.id::text) AS DocumentDisplay,
        h.document_date_utc AS DocumentDateUtc,
        d.updated_at_utc AS UpdatedAtUtc,
        CASE d.status
            WHEN 1 THEN 'Draft'
            WHEN 2 THEN 'Posted'
            WHEN 3 THEN 'Marked for deletion'
            ELSE d.status::text
        END AS StatusDisplay,
        partner.display AS PartnerDisplay,
        h.amount AS HeaderAmount
    FROM documents d
    INNER JOIN doc_trd_customer_return h
        ON h.document_id = d.id
    LEFT JOIN cat_trd_party partner
        ON partner.catalog_id = h.customer_id
    WHERE d.type_code = @customer_return_type
      AND d.status <> @deleted_status
      AND h.document_date_utc <= @as_of_utc

    UNION ALL

    SELECT
        d.id AS DocumentId,
        d.type_code AS DocumentTypeCode,
        'Vendor Return' AS DocumentTypeDisplay,
        COALESCE(h.display, d.number, d.id::text) AS DocumentDisplay,
        h.document_date_utc AS DocumentDateUtc,
        d.updated_at_utc AS UpdatedAtUtc,
        CASE d.status
            WHEN 1 THEN 'Draft'
            WHEN 2 THEN 'Posted'
            WHEN 3 THEN 'Marked for deletion'
            ELSE d.status::text
        END AS StatusDisplay,
        partner.display AS PartnerDisplay,
        h.amount AS HeaderAmount
    FROM documents d
    INNER JOIN doc_trd_vendor_return h
        ON h.document_id = d.id
    LEFT JOIN cat_trd_party partner
        ON partner.catalog_id = h.vendor_id
    WHERE d.type_code = @vendor_return_type
      AND d.status <> @deleted_status
      AND h.document_date_utc <= @as_of_utc
),
recent AS (
    SELECT *
    FROM recent_candidates
    ORDER BY UpdatedAtUtc DESC, DocumentDateUtc DESC, DocumentDisplay ASC
    LIMIT @limit
)
SELECT
    DocumentId,
    DocumentTypeCode,
    DocumentTypeDisplay,
    DocumentDisplay,
    DocumentDateUtc,
    UpdatedAtUtc,
    StatusDisplay,
    PartnerDisplay,
    HeaderAmount AS Amount
FROM recent
ORDER BY UpdatedAtUtc DESC, DocumentDateUtc DESC, DocumentDisplay ASC;
""";

        var rows = await uow.Connection.QueryAsync<RecentTradeDocumentSummaryRow>(new CommandDefinition(
            sql,
            new
            {
                purchase_receipt_type = TradeCodes.PurchaseReceipt,
                sales_invoice_type = TradeCodes.SalesInvoice,
                customer_payment_type = TradeCodes.CustomerPayment,
                vendor_payment_type = TradeCodes.VendorPayment,
                customer_return_type = TradeCodes.CustomerReturn,
                vendor_return_type = TradeCodes.VendorReturn,
                deleted_status = (short)DocumentStatus.MarkedForDeletion,
                as_of_utc = asOf,
                limit = Math.Max(1, limit)
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows.ToArray();
    }

    private static Guid[] NormalizeIds(IReadOnlyList<Guid>? ids)
        => ids?
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray()
           ?? [];

    private sealed record SalesByItemPageSqlRow(
        Guid ItemId,
        string ItemDisplay,
        decimal SoldQuantity,
        decimal GrossSales,
        decimal ReturnedQuantity,
        decimal ReturnedAmount,
        decimal NetSales,
        decimal NetCogs,
        int TotalCount,
        decimal TotalSoldQuantity,
        decimal TotalGrossSales,
        decimal TotalReturnedQuantity,
        decimal TotalReturnedAmount,
        decimal TotalNetSales,
        decimal TotalNetCogs);

    private sealed record SalesByCustomerPageSqlRow(
        Guid CustomerId,
        string CustomerDisplay,
        int SalesDocumentCount,
        int ReturnDocumentCount,
        decimal GrossSales,
        decimal ReturnedAmount,
        decimal NetSales,
        decimal NetCogs,
        int TotalCount,
        int TotalSalesDocumentCount,
        int TotalReturnDocumentCount,
        decimal TotalGrossSales,
        decimal TotalReturnedAmount,
        decimal TotalNetSales,
        decimal TotalNetCogs);

    private sealed record PurchasesByVendorPageSqlRow(
        Guid VendorId,
        string VendorDisplay,
        int PurchaseDocumentCount,
        int ReturnDocumentCount,
        decimal GrossPurchases,
        decimal ReturnedAmount,
        decimal NetPurchases,
        int TotalCount,
        int TotalPurchaseDocumentCount,
        int TotalReturnDocumentCount,
        decimal TotalGrossPurchases,
        decimal TotalReturnedAmount,
        decimal TotalNetPurchases);

    private sealed record DashboardPurchasesSqlRow(decimal NetPurchases);
}
