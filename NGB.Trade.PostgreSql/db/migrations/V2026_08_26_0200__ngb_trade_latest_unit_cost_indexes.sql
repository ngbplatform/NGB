-- Supports latest unit-cost lookup by the requested (warehouse, item) pairs.
-- Header indexes restrict candidates by warehouse/date before joining line rows.
CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__warehouse_date_document
    ON doc_trd_purchase_receipt(warehouse_id, document_date_utc DESC, document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__warehouse_date_document
    ON doc_trd_sales_invoice(warehouse_id, document_date_utc DESC, document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__warehouse_date_document
    ON doc_trd_customer_return(warehouse_id, document_date_utc DESC, document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__warehouse_date_document
    ON doc_trd_vendor_return(warehouse_id, document_date_utc DESC, document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__warehouse_date_document
    ON doc_trd_inventory_adjustment(warehouse_id, document_date_utc DESC, document_id);

-- Line indexes avoid scanning every line of candidate documents for one requested item.
CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__lines__item_document
    ON doc_trd_purchase_receipt__lines(item_id, document_id)
    INCLUDE (unit_cost, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__lines__item_document
    ON doc_trd_sales_invoice__lines(item_id, document_id)
    INCLUDE (unit_cost, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__lines__item_document
    ON doc_trd_customer_return__lines(item_id, document_id)
    INCLUDE (unit_cost, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__lines__item_document
    ON doc_trd_vendor_return__lines(item_id, document_id)
    INCLUDE (unit_cost, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__lines__item_document
    ON doc_trd_inventory_adjustment__lines(item_id, document_id)
    INCLUDE (unit_cost, ordinal);
