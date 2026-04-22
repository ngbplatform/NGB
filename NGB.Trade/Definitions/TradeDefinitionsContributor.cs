using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Trade.Documents.Numbering;

namespace NGB.Trade.Definitions;

/// <summary>
/// Trade module definitions for the production-ready demo slice:
/// master data, accounting policy, pricing, inventory, payments, and returns/corrections.
/// </summary>
public sealed class TradeDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddCatalog(TradeCodes.Party, c => c.Metadata(BuildParty()));
        builder.AddCatalog(TradeCodes.Item, c => c.Metadata(BuildItem()));
        builder.AddCatalog(TradeCodes.Warehouse, c => c.Metadata(BuildWarehouse()));
        builder.AddCatalog(TradeCodes.UnitOfMeasure, c => c.Metadata(BuildUnitOfMeasure()));
        builder.AddCatalog(TradeCodes.PaymentTerms, c => c.Metadata(BuildPaymentTerms()));
        builder.AddCatalog(TradeCodes.InventoryAdjustmentReason, c => c.Metadata(BuildInventoryAdjustmentReason()));
        builder.AddCatalog(TradeCodes.PriceType, c => c.Metadata(BuildPriceType()));
        builder.AddCatalog(TradeCodes.AccountingPolicy, c => c.Metadata(BuildAccountingPolicy()));

        builder.AddDocument(TradeCodes.PurchaseReceipt, d => d
            .Metadata(BuildPurchaseReceipt())
            .NumberingPolicy<TrdPurchaseReceiptNumberingPolicy>());
        builder.AddDocument(TradeCodes.SalesInvoice, d => d
            .Metadata(BuildSalesInvoice())
            .NumberingPolicy<TrdSalesInvoiceNumberingPolicy>());
        builder.AddDocument(TradeCodes.CustomerPayment, d => d
            .Metadata(BuildCustomerPayment())
            .NumberingPolicy<TradeCustomerPaymentNumberingPolicy>());
        builder.AddDocument(TradeCodes.VendorPayment, d => d
            .Metadata(BuildVendorPayment())
            .NumberingPolicy<TradeVendorPaymentNumberingPolicy>());
        builder.AddDocument(TradeCodes.InventoryTransfer, d => d
            .Metadata(BuildInventoryTransfer())
            .NumberingPolicy<TradeInventoryTransferNumberingPolicy>());
        builder.AddDocument(TradeCodes.InventoryAdjustment, d => d
            .Metadata(BuildInventoryAdjustment())
            .NumberingPolicy<TradeInventoryAdjustmentNumberingPolicy>());
        builder.AddDocument(TradeCodes.CustomerReturn, d => d
            .Metadata(BuildCustomerReturn())
            .NumberingPolicy<TradeCustomerReturnNumberingPolicy>());
        builder.AddDocument(TradeCodes.VendorReturn, d => d
            .Metadata(BuildVendorReturn())
            .NumberingPolicy<TradeVendorReturnNumberingPolicy>());
        builder.AddDocument(TradeCodes.ItemPriceUpdate, d => d
            .Metadata(BuildItemPriceUpdate())
            .NumberingPolicy<TrdItemPriceUpdateNumberingPolicy>());
    }

    private static CatalogLookupSourceMetadata CatalogLookup(string catalogType) => new(catalogType);
    private static DocumentLookupSourceMetadata DocumentLookup(params string[] documentTypes) => new(documentTypes);
    private static ChartOfAccountsLookupSourceMetadata CoaLookup() => new();
    private static DocumentListFilterMetadata ListLookupFilter(string key, string label, string catalogType)
        => new(
            Key: key,
            Label: label,
            Type: ColumnType.Guid,
            IsMulti: true,
            Lookup: CatalogLookup(catalogType));
    private static DocumentListFilterMetadata ListDocumentFilter(string key, string label, params string[] documentTypes)
        => new(
            Key: key,
            Label: label,
            Type: ColumnType.Guid,
            IsMulti: true,
            Lookup: DocumentLookup(documentTypes));
    private static DocumentListFilterMetadata ListCoaFilter(string key, string label)
        => new(
            Key: key,
            Label: label,
            Type: ColumnType.Guid,
            IsMulti: true,
            Lookup: CoaLookup());

    private static CatalogTypeMetadata BuildParty()
        => new(
            CatalogCode: TradeCodes.Party,
            DisplayName: "Party",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_party",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("party_number", ColumnType.String),
                        new("name", ColumnType.String, Required: true),
                        new("legal_name", ColumnType.String),
                        new("email", ColumnType.String),
                        new("phone", ColumnType.String),
                        new("tax_id", ColumnType.String, UiLabel: "Tax ID / EIN"),
                        new("billing_address", ColumnType.String),
                        new("shipping_address", ColumnType.String),
                        new("payment_terms_id", ColumnType.Guid, Lookup: CatalogLookup(TradeCodes.PaymentTerms)),
                        new("default_currency", ColumnType.String),
                        new("is_customer", ColumnType.Boolean, Required: true),
                        new("is_vendor", ColumnType.Boolean, Required: true),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_party__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_party__party_number", ["party_number"]),
                        new CatalogIndexMetadata("ix_cat_trd_party__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_trd_party__payment_terms_id", ["payment_terms_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_party__is_customer", ["is_customer"]),
                        new CatalogIndexMetadata("ix_cat_trd_party__is_vendor", ["is_vendor"]),
                        new CatalogIndexMetadata("ix_cat_trd_party__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_party", "display"),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static CatalogTypeMetadata BuildItem()
        => new(
            CatalogCode: TradeCodes.Item,
            DisplayName: "Item",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_item",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("sku", ColumnType.String),
                        new("unit_of_measure_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.UnitOfMeasure)),
                        new("item_type", ColumnType.String),
                        new("is_inventory_item", ColumnType.Boolean, Required: true),
                        new("default_sales_price_type_id", ColumnType.Guid, Lookup: CatalogLookup(TradeCodes.PriceType)),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_item__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_item__sku", ["sku"]),
                        new CatalogIndexMetadata("ix_cat_trd_item__unit_of_measure_id", ["unit_of_measure_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_item__default_sales_price_type_id", ["default_sales_price_type_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_item__is_inventory_item", ["is_inventory_item"]),
                        new CatalogIndexMetadata("ix_cat_trd_item__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_item", "display"),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static CatalogTypeMetadata BuildWarehouse()
        => new(
            CatalogCode: TradeCodes.Warehouse,
            DisplayName: "Warehouse",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_warehouse",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("warehouse_code", ColumnType.String),
                        new("name", ColumnType.String, Required: true),
                        new("address", ColumnType.String),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_warehouse__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_warehouse__warehouse_code", ["warehouse_code"]),
                        new CatalogIndexMetadata("ix_cat_trd_warehouse__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_trd_warehouse__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_warehouse", "display", ComputedDisplay: true),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static CatalogTypeMetadata BuildUnitOfMeasure()
        => new(
            CatalogCode: TradeCodes.UnitOfMeasure,
            DisplayName: "Unit of Measure",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_unit_of_measure",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("code", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("symbol", ColumnType.String),
                        new("is_active", ColumnType.Boolean, Required: true)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_unit_of_measure__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_unit_of_measure__code", ["code"]),
                        new CatalogIndexMetadata("ix_cat_trd_unit_of_measure__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_trd_unit_of_measure__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_unit_of_measure", "display"),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static CatalogTypeMetadata BuildPaymentTerms()
        => new(
            CatalogCode: TradeCodes.PaymentTerms,
            DisplayName: "Payment Terms",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_payment_terms",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("code", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("due_days", ColumnType.Int32, Required: true),
                        new("is_active", ColumnType.Boolean, Required: true)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_payment_terms__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_payment_terms__code", ["code"]),
                        new CatalogIndexMetadata("ix_cat_trd_payment_terms__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_trd_payment_terms__due_days", ["due_days"]),
                        new CatalogIndexMetadata("ix_cat_trd_payment_terms__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_payment_terms", "display"),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static CatalogTypeMetadata BuildInventoryAdjustmentReason()
        => new(
            CatalogCode: TradeCodes.InventoryAdjustmentReason,
            DisplayName: "Inventory Adjustment Reason",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_inventory_adjustment_reason",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("code", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("gl_behavior_hint", ColumnType.String),
                        new("is_active", ColumnType.Boolean, Required: true)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_inventory_adjustment_reason__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_inventory_adjustment_reason__code", ["code"]),
                        new CatalogIndexMetadata("ix_cat_trd_inventory_adjustment_reason__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_trd_inventory_adjustment_reason__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_inventory_adjustment_reason", "display"),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static CatalogTypeMetadata BuildPriceType()
        => new(
            CatalogCode: TradeCodes.PriceType,
            DisplayName: "Price Type",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_price_type",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("code", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("currency", ColumnType.String, Required: true),
                        new("is_default", ColumnType.Boolean, Required: true),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_price_type__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_price_type__code", ["code"]),
                        new CatalogIndexMetadata("ix_cat_trd_price_type__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_trd_price_type__currency", ["currency"]),
                        new CatalogIndexMetadata("ix_cat_trd_price_type__is_default", ["is_default"]),
                        new CatalogIndexMetadata("ix_cat_trd_price_type__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_price_type", "display"),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static CatalogTypeMetadata BuildAccountingPolicy()
        => new(
            CatalogCode: TradeCodes.AccountingPolicy,
            DisplayName: "Accounting Policy",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_trd_accounting_policy",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("cash_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("ar_account_id", ColumnType.Guid, UiLabel: "Accounts Receivable", Lookup: CoaLookup()),
                        new("inventory_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("ap_account_id", ColumnType.Guid, UiLabel: "Accounts Payable", Lookup: CoaLookup()),
                        new("sales_revenue_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("cogs_account_id", ColumnType.Guid, UiLabel: "COGS", Lookup: CoaLookup()),
                        new("inventory_adjustment_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("inventory_movements_register_id", ColumnType.Guid),
                        new("item_prices_register_id", ColumnType.Guid)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__cash_account_id", ["cash_account_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__ar_account_id", ["ar_account_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__inventory_account_id", ["inventory_account_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__ap_account_id", ["ap_account_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__sales_revenue_account_id", ["sales_revenue_account_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__cogs_account_id", ["cogs_account_id"]),
                        new CatalogIndexMetadata("ix_cat_trd_accounting_policy__inventory_adjustment_account_id", ["inventory_adjustment_account_id"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_trd_accounting_policy", "display"),
            Version: new CatalogMetadataVersion(1, "trd"));

    private static DocumentTypeMetadata BuildPurchaseReceipt()
        => new(
            TypeCode: TradeCodes.PurchaseReceipt,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_purchase_receipt",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("vendor_id", ColumnType.Guid, Required: true, UiLabel: "Vendor", Lookup: CatalogLookup(TradeCodes.Party)),
                        new("warehouse_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Warehouse)),
                        new("notes", ColumnType.String),
                        new("amount", ColumnType.Decimal)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_purchase_receipt__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_purchase_receipt__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_purchase_receipt__vendor_id", ["vendor_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_purchase_receipt__warehouse_id", ["warehouse_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_trd_purchase_receipt__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("item_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Item)),
                        new("quantity", ColumnType.Decimal, Required: true),
                        new("unit_cost", ColumnType.Decimal, Required: true),
                        new("line_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_purchase_receipt__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_purchase_receipt__lines__item_id", ["item_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Purchase Receipt", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("vendor_id", "Vendor", TradeCodes.Party),
                ListLookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse)
            ]);

    private static DocumentTypeMetadata BuildSalesInvoice()
        => new(
            TypeCode: TradeCodes.SalesInvoice,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_sales_invoice",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("customer_id", ColumnType.Guid, Required: true, UiLabel: "Customer", Lookup: CatalogLookup(TradeCodes.Party)),
                        new("warehouse_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Warehouse)),
                        new("price_type_id", ColumnType.Guid, Lookup: CatalogLookup(TradeCodes.PriceType)),
                        new("notes", ColumnType.String),
                        new("amount", ColumnType.Decimal)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_sales_invoice__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_sales_invoice__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_sales_invoice__customer_id", ["customer_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_sales_invoice__warehouse_id", ["warehouse_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_sales_invoice__price_type_id", ["price_type_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_trd_sales_invoice__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("item_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Item)),
                        new("quantity", ColumnType.Decimal, Required: true),
                        new("unit_price", ColumnType.Decimal, Required: true),
                        new("unit_cost", ColumnType.Decimal, Required: true, UiLabel: "Unit Cost Snapshot"),
                        new("line_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_sales_invoice__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_sales_invoice__lines__item_id", ["item_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Sales Invoice", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("customer_id", "Customer", TradeCodes.Party),
                ListLookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse),
                ListLookupFilter("price_type_id", "Price Type", TradeCodes.PriceType)
            ]);

    private static DocumentTypeMetadata BuildCustomerPayment()
        => new(
            TypeCode: TradeCodes.CustomerPayment,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_customer_payment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("customer_id", ColumnType.Guid, Required: true, UiLabel: "Customer", Lookup: CatalogLookup(TradeCodes.Party)),
                        new("cash_account_id", ColumnType.Guid, UiLabel: "Cash / Bank Account", Lookup: CoaLookup()),
                        new("sales_invoice_id", ColumnType.Guid, UiLabel: "Related Sales Invoice", Lookup: DocumentLookup(TradeCodes.SalesInvoice), MirroredRelationship: new MirroredDocumentRelationshipMetadata("based_on")),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_customer_payment__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_payment__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_payment__customer_id", ["customer_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_payment__cash_account_id", ["cash_account_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_payment__sales_invoice_id", ["sales_invoice_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Customer Payment", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("customer_id", "Customer", TradeCodes.Party),
                ListCoaFilter("cash_account_id", "Cash / Bank Account"),
                ListDocumentFilter("sales_invoice_id", "Sales Invoice", TradeCodes.SalesInvoice)
            ]);

    private static DocumentTypeMetadata BuildVendorPayment()
        => new(
            TypeCode: TradeCodes.VendorPayment,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_vendor_payment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("vendor_id", ColumnType.Guid, Required: true, UiLabel: "Vendor", Lookup: CatalogLookup(TradeCodes.Party)),
                        new("cash_account_id", ColumnType.Guid, UiLabel: "Cash / Bank Account", Lookup: CoaLookup()),
                        new("purchase_receipt_id", ColumnType.Guid, UiLabel: "Related Purchase Receipt", Lookup: DocumentLookup(TradeCodes.PurchaseReceipt), MirroredRelationship: new MirroredDocumentRelationshipMetadata("based_on")),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_vendor_payment__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_payment__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_payment__vendor_id", ["vendor_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_payment__cash_account_id", ["cash_account_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_payment__purchase_receipt_id", ["purchase_receipt_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Vendor Payment", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("vendor_id", "Vendor", TradeCodes.Party),
                ListCoaFilter("cash_account_id", "Cash / Bank Account"),
                ListDocumentFilter("purchase_receipt_id", "Purchase Receipt", TradeCodes.PurchaseReceipt)
            ]);

    private static DocumentTypeMetadata BuildInventoryTransfer()
        => new(
            TypeCode: TradeCodes.InventoryTransfer,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_inventory_transfer",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("from_warehouse_id", ColumnType.Guid, Required: true, UiLabel: "From Warehouse", Lookup: CatalogLookup(TradeCodes.Warehouse)),
                        new("to_warehouse_id", ColumnType.Guid, Required: true, UiLabel: "To Warehouse", Lookup: CatalogLookup(TradeCodes.Warehouse)),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_inventory_transfer__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_transfer__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_transfer__from_warehouse_id", ["from_warehouse_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_transfer__to_warehouse_id", ["to_warehouse_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_trd_inventory_transfer__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("item_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Item)),
                        new("quantity", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_inventory_transfer__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_transfer__lines__item_id", ["item_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Inventory Transfer", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("from_warehouse_id", "From Warehouse", TradeCodes.Warehouse),
                ListLookupFilter("to_warehouse_id", "To Warehouse", TradeCodes.Warehouse)
            ]);

    private static DocumentTypeMetadata BuildInventoryAdjustment()
        => new(
            TypeCode: TradeCodes.InventoryAdjustment,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_inventory_adjustment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("warehouse_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Warehouse)),
                        new("reason_id", ColumnType.Guid, Required: true, UiLabel: "Reason", Lookup: CatalogLookup(TradeCodes.InventoryAdjustmentReason)),
                        new("notes", ColumnType.String),
                        new("amount", ColumnType.Decimal)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_inventory_adjustment__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_adjustment__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_adjustment__warehouse_id", ["warehouse_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_adjustment__reason_id", ["reason_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_trd_inventory_adjustment__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("item_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Item)),
                        new("quantity_delta", ColumnType.Decimal, Required: true, UiLabel: "Quantity Delta"),
                        new("unit_cost", ColumnType.Decimal, Required: true),
                        new("line_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_inventory_adjustment__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_inventory_adjustment__lines__item_id", ["item_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Inventory Adjustment", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse),
                ListLookupFilter("reason_id", "Reason", TradeCodes.InventoryAdjustmentReason)
            ]);

    private static DocumentTypeMetadata BuildCustomerReturn()
        => new(
            TypeCode: TradeCodes.CustomerReturn,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_customer_return",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("customer_id", ColumnType.Guid, Required: true, UiLabel: "Customer", Lookup: CatalogLookup(TradeCodes.Party)),
                        new("warehouse_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Warehouse)),
                        new("sales_invoice_id", ColumnType.Guid, UiLabel: "Related Sales Invoice", Lookup: DocumentLookup(TradeCodes.SalesInvoice), MirroredRelationship: new MirroredDocumentRelationshipMetadata("based_on")),
                        new("notes", ColumnType.String),
                        new("amount", ColumnType.Decimal)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_customer_return__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_return__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_return__customer_id", ["customer_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_return__warehouse_id", ["warehouse_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_return__sales_invoice_id", ["sales_invoice_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_trd_customer_return__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("item_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Item)),
                        new("quantity", ColumnType.Decimal, Required: true),
                        new("unit_price", ColumnType.Decimal, Required: true),
                        new("unit_cost", ColumnType.Decimal, Required: true, UiLabel: "Unit Cost Snapshot"),
                        new("line_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_customer_return__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_customer_return__lines__item_id", ["item_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Customer Return", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("customer_id", "Customer", TradeCodes.Party),
                ListLookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse),
                ListDocumentFilter("sales_invoice_id", "Sales Invoice", TradeCodes.SalesInvoice)
            ]);

    private static DocumentTypeMetadata BuildVendorReturn()
        => new(
            TypeCode: TradeCodes.VendorReturn,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_vendor_return",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("vendor_id", ColumnType.Guid, Required: true, UiLabel: "Vendor", Lookup: CatalogLookup(TradeCodes.Party)),
                        new("warehouse_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Warehouse)),
                        new("purchase_receipt_id", ColumnType.Guid, UiLabel: "Related Purchase Receipt", Lookup: DocumentLookup(TradeCodes.PurchaseReceipt), MirroredRelationship: new MirroredDocumentRelationshipMetadata("based_on")),
                        new("notes", ColumnType.String),
                        new("amount", ColumnType.Decimal)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_vendor_return__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_return__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_return__vendor_id", ["vendor_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_return__warehouse_id", ["warehouse_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_return__purchase_receipt_id", ["purchase_receipt_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_trd_vendor_return__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("item_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Item)),
                        new("quantity", ColumnType.Decimal, Required: true),
                        new("unit_cost", ColumnType.Decimal, Required: true),
                        new("line_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_vendor_return__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_vendor_return__lines__item_id", ["item_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Vendor Return", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "trd"),
            ListFilters:
            [
                ListLookupFilter("vendor_id", "Vendor", TradeCodes.Party),
                ListLookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse),
                ListDocumentFilter("purchase_receipt_id", "Purchase Receipt", TradeCodes.PurchaseReceipt)
            ]);

    private static DocumentTypeMetadata BuildItemPriceUpdate()
        => new(
            TypeCode: TradeCodes.ItemPriceUpdate,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_trd_item_price_update",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("effective_date", ColumnType.Date, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_item_price_update__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_trd_item_price_update__effective_date", ["effective_date"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_trd_item_price_update__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("item_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.Item)),
                        new("price_type_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(TradeCodes.PriceType)),
                        new("currency", ColumnType.String, Required: true),
                        new("unit_price", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_trd_item_price_update__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_item_price_update__lines__item_id", ["item_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_item_price_update__lines__price_type_id", ["price_type_id"]),
                        new DocumentIndexMetadata("ix_doc_trd_item_price_update__lines__currency", ["currency"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Item Price Update", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(1, "trd"));
}
