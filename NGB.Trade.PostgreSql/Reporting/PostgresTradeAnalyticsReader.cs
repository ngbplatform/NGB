using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Trade.Reporting;

namespace NGB.Trade.PostgreSql.Reporting;

public sealed class PostgresTradeAnalyticsReader(IUnitOfWork uow) : ITradeAnalyticsReader
{
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
                offset,
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
                offset,
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
                offset,
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
        NULL::numeric AS HeaderAmount
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
        NULL::numeric AS HeaderAmount
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
        NULL::numeric AS HeaderAmount
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
        NULL::numeric AS HeaderAmount
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
    CASE DocumentTypeCode
        WHEN @purchase_receipt_type THEN (
            SELECT SUM(line.line_amount)
            FROM doc_trd_purchase_receipt__lines line
            WHERE line.document_id = recent.DocumentId)
        WHEN @sales_invoice_type THEN (
            SELECT SUM(line.line_amount)
            FROM doc_trd_sales_invoice__lines line
            WHERE line.document_id = recent.DocumentId)
        WHEN @customer_return_type THEN (
            SELECT SUM(line.line_amount)
            FROM doc_trd_customer_return__lines line
            WHERE line.document_id = recent.DocumentId)
        WHEN @vendor_return_type THEN (
            SELECT SUM(line.line_amount)
            FROM doc_trd_vendor_return__lines line
            WHERE line.document_id = recent.DocumentId)
        ELSE HeaderAmount
    END AS Amount
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
}
