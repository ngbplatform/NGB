using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.PropertyManagement.Documents.Numbering;

namespace NGB.PropertyManagement.Definitions;

/// <summary>
/// Property Management module registrations.
/// </summary>
public sealed class PropertyManagementDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        // catalogs
        builder.AddCatalog(PropertyManagementCodes.Party, c => c.Metadata(BuildParty()));
        builder.AddCatalog(PropertyManagementCodes.Property, c => c.Metadata(BuildProperty()));
        builder.AddCatalog(PropertyManagementCodes.BankAccount, c => c.Metadata(BuildBankAccount()));
        builder.AddCatalog(PropertyManagementCodes.MaintenanceCategory, c => c.Metadata(BuildMaintenanceCategory()));

        // accounting policy (single-record catalog)
        builder.AddCatalog(PropertyManagementCodes.AccountingPolicy, c => c.Metadata(BuildAccountingPolicy()));

        // receivables / payables configuration (charge types)
        builder.AddCatalog(PropertyManagementCodes.ReceivableChargeType, c => c.Metadata(BuildReceivableChargeType()));
        builder.AddCatalog(PropertyManagementCodes.PayableChargeType, c => c.Metadata(BuildPayableChargeType()));

        // documents
        builder.AddDocument(PropertyManagementCodes.Lease, d => d.Metadata(BuildLease()));

        // documents: Maintenance
        builder.AddDocument(PropertyManagementCodes.MaintenanceRequest, d => d
            .Metadata(BuildMaintenanceRequest())
            .NumberingPolicy<PmMaintenanceRequestNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.WorkOrder, d => d
            .Metadata(BuildWorkOrder())
            .NumberingPolicy<PmWorkOrderNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.WorkOrderCompletion, d => d
            .Metadata(BuildWorkOrderCompletion())
            .NumberingPolicy<PmWorkOrderCompletionNumberingPolicy>());

        // documents: Receivables
        builder.AddDocument(PropertyManagementCodes.RentCharge, d => d
            .Metadata(BuildRentCharge())
            .NumberingPolicy<PmRentChargeNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.ReceivableCharge, d => d
            .Metadata(BuildReceivableCharge())
            .NumberingPolicy<PmReceivableChargeNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.LateFeeCharge, d => d
            .Metadata(BuildLateFeeCharge())
            .NumberingPolicy<PmLateFeeChargeNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.ReceivablePayment, d => d
            .Metadata(BuildReceivablePayment())
            .NumberingPolicy<PmReceivablePaymentNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.ReceivableReturnedPayment, d => d
            .Metadata(BuildReceivableReturnedPayment())
            .NumberingPolicy<PmReceivableReturnedPaymentNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.ReceivableCreditMemo, d => d
            .Metadata(BuildReceivableCreditMemo())
            .NumberingPolicy<PmReceivableCreditMemoNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.ReceivableApply, d => d
            .Metadata(BuildReceivableApply())
            .NumberingPolicy<PmReceivableApplyNumberingPolicy>());

        // documents: Payables
        builder.AddDocument(PropertyManagementCodes.PayableCharge, d => d
            .Metadata(BuildPayableCharge())
            .NumberingPolicy<PmPayableChargeNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.PayablePayment, d => d
            .Metadata(BuildPayablePayment())
            .NumberingPolicy<PmPayablePaymentNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.PayableCreditMemo, d => d
            .Metadata(BuildPayableCreditMemo())
            .NumberingPolicy<PmPayableCreditMemoNumberingPolicy>());
        builder.AddDocument(PropertyManagementCodes.PayableApply, d => d
            .Metadata(BuildPayableApply())
            .NumberingPolicy<PmPayableApplyNumberingPolicy>());
    }

    private static CatalogLookupSourceMetadata CatalogLookup(string catalogType) => new(catalogType);
    private static DocumentLookupSourceMetadata DocumentLookup(params string[] documentTypes) => new(documentTypes);
    private static ChartOfAccountsLookupSourceMetadata CoaLookup() => new();

    private static DocumentListFilterMetadata ListDocumentFilter(
        string key,
        string label,
        params string[] documentTypes)
        => new(
            Key: key,
            Label: label,
            Type: ColumnType.Guid,
            IsMulti: true,
            Lookup: DocumentLookup(documentTypes));

    private static DocumentListFilterMetadata ListLookupFilter(
        string key,
        string label,
        string catalogType,
        string? description = null)
        => new(
            Key: key,
            Label: label,
            Type: ColumnType.Guid,
            IsMulti: true,
            Lookup: CatalogLookup(catalogType),
            Description: description);

    private static DocumentListFilterMetadata ListOptionFilter(
        string key,
        string label,
        params DocumentListFilterOptionMetadata[] options)
        => new(Key: key, Label: label, Type: ColumnType.String, Options: options);

    private static DocumentListFilterMetadata ListAmountFilter(string key, string label = "Amount")
        => new(Key: key, Label: label, Type: ColumnType.Decimal);

    private static readonly DocumentListFilterOptionMetadata[] MaintenancePriorityOptions =
    [
        new("Emergency", "Emergency"),
        new("High", "High"),
        new("Normal", "Normal"),
        new("Low", "Low")
    ];

    private static readonly DocumentListFilterOptionMetadata[] WorkOrderOutcomeOptions =
    [
        new("Completed", "Completed"),
        new("Cancelled", "Cancelled"),
        new("UnableToComplete", "Unable to complete")
    ];

    private static CatalogTypeMetadata BuildParty()
        => new(
            CatalogCode: PropertyManagementCodes.Party,
            DisplayName: "Party",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_pm_party",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("email", ColumnType.String),
                        new("phone", ColumnType.String),
                        new("is_tenant", ColumnType.Boolean, UiLabel: "Tenant"),
                        new("is_vendor", ColumnType.Boolean, UiLabel: "Vendor")
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_pm_party__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_pm_party__is_tenant", ["is_tenant"]),
                        new CatalogIndexMetadata("ix_cat_pm_party__is_vendor", ["is_vendor"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_pm_party", "display"),
            Version: new CatalogMetadataVersion(2, "pm"));

    private static CatalogTypeMetadata BuildProperty()
        => new(
            CatalogCode: PropertyManagementCodes.Property,
            DisplayName: "Property",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_pm_property",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        // One catalog for Building & Unit (Unit has parent Building).
                        new("kind", ColumnType.String, Required: true),
                        new("parent_property_id", ColumnType.Guid, Lookup: CatalogLookup(PropertyManagementCodes.Property)),
                        new("unit_no", ColumnType.String),
                        // DB-computed
                        new("display", ColumnType.String),
                        // Building address (required in DB when kind='Building').
                        new("address_line1", ColumnType.String),
                        new("address_line2", ColumnType.String),
                        new("city", ColumnType.String),
                        new("state", ColumnType.String),
                        new("zip", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_pm_property__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_pm_property__kind", ["kind"]),
                        new CatalogIndexMetadata("ix_cat_pm_property__parent_property_id", ["parent_property_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_property__parent_unit_no", ["parent_property_id", "unit_no"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_pm_property", "display", ComputedDisplay: true),
            Version: new CatalogMetadataVersion(3, "pm"));

    private static CatalogTypeMetadata BuildBankAccount()
        => new(
            CatalogCode: PropertyManagementCodes.BankAccount,
            DisplayName: "Bank Account",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_pm_bank_account",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("bank_name", ColumnType.String, Required: true),
                        new("account_name", ColumnType.String, Required: true),
                        new("last4", ColumnType.String, Required: true, UiLabel: "Last 4 digits"),
                        new("gl_account_id", ColumnType.Guid, Required: true, UiLabel: "GL Account", Lookup: CoaLookup()),
                        new("is_default", ColumnType.Boolean, Required: true)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_pm_bank_account__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_pm_bank_account__bank_name", ["bank_name"]),
                        new CatalogIndexMetadata("ix_cat_pm_bank_account__gl_account_id", ["gl_account_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_bank_account__is_default", ["is_default"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_pm_bank_account", "display", ComputedDisplay: true),
            Version: new CatalogMetadataVersion(3, "pm"));

    private static CatalogTypeMetadata BuildMaintenanceCategory()
        => new(
            CatalogCode: PropertyManagementCodes.MaintenanceCategory,
            DisplayName: "Maintenance Category",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_pm_maintenance_category",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_pm_maintenance_category__display", ["display"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_pm_maintenance_category", "display"),
            Version: new CatalogMetadataVersion(1, "pm"));

    private static CatalogTypeMetadata BuildAccountingPolicy()
        => new(
            CatalogCode: PropertyManagementCodes.AccountingPolicy,
            DisplayName: "Accounting Policy",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_pm_accounting_policy",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),

                        // Accounting
                        new("cash_account_id", ColumnType.Guid, Required: false, Lookup: CoaLookup()),
                        new("ar_tenants_account_id", ColumnType.Guid, Required: false, UiLabel: "AR Tenants Account", Lookup: CoaLookup()),
                        new("ap_vendors_account_id", ColumnType.Guid, Required: false, UiLabel: "AP Vendors Account", Lookup: CoaLookup()),
                        new("rent_income_account_id", ColumnType.Guid, Required: false, Lookup: CoaLookup()),
                        new("late_fee_income_account_id", ColumnType.Guid, Required: false, Lookup: CoaLookup()),

                        // Operational Registers
                        new("tenant_balances_register_id", ColumnType.Guid, Required: false),

                        // Receivables / Payables (open items)
                        new("receivables_open_items_register_id", ColumnType.Guid, Required: false),
                        new("payables_open_items_register_id", ColumnType.Guid, Required: false)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__cash_account_id", ["cash_account_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__ar_tenants_account_id", ["ar_tenants_account_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__ap_vendors_account_id", ["ap_vendors_account_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__rent_income_account_id", ["rent_income_account_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__late_fee_income_account_id", ["late_fee_income_account_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__tenant_balances_register_id", ["tenant_balances_register_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__receivables_open_items_register_id", ["receivables_open_items_register_id"]),
                        new CatalogIndexMetadata("ix_cat_pm_accounting_policy__payables_open_items_register_id", ["payables_open_items_register_id"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_pm_accounting_policy", "display"),
            Version: new CatalogMetadataVersion(6, "pm"));

    private static CatalogTypeMetadata BuildReceivableChargeType()
        => new(
            CatalogCode: PropertyManagementCodes.ReceivableChargeType,
            DisplayName: "Receivable Charge Type",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_pm_receivable_charge_type",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("credit_account_id", ColumnType.Guid, Required: false)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_pm_receivable_charge_type__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_pm_receivable_charge_type__credit_account_id", ["credit_account_id"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_pm_receivable_charge_type", "display"),
            Version: new CatalogMetadataVersion(3, "pm"));

    private static CatalogTypeMetadata BuildPayableChargeType()
        => new(
            CatalogCode: PropertyManagementCodes.PayableChargeType,
            DisplayName: "Payable Charge Type",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_pm_payable_charge_type",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("debit_account_id", ColumnType.Guid, Required: false, Lookup: CoaLookup())
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_pm_payable_charge_type__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_pm_payable_charge_type__debit_account_id", ["debit_account_id"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_pm_payable_charge_type", "display"),
            Version: new CatalogMetadataVersion(2, "pm"));

    private static DocumentTypeMetadata BuildLease()
        => new(
            TypeCode: PropertyManagementCodes.Lease,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_lease",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("property_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.Property)),
                        new("start_on_utc", ColumnType.Date, Required: true),
                        new("end_on_utc", ColumnType.Date),
                        new("rent_amount", ColumnType.Decimal, Required: true),
                        new("due_day", ColumnType.Int32),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_lease__display", ["display"])
                    ]),

                // multi-tenants: tenants/occupants/guarantors
                new DocumentTableMetadata(
                    TableName: "doc_pm_lease__parties",
                    Kind: TableKind.Part,
                    PartCode: "parties",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("party_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.Party)),
                        new("role", ColumnType.String, Required: true),
                        new("is_primary", ColumnType.Boolean, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_lease__parties__party_id", ["party_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_lease__parties__document_id", ["document_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Lease", HasNumber: false, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "rent_amount"),
            Version: new DocumentMetadataVersion(2, "pm"),
            ListFilters:
            [
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party)
            ]);

    private static DocumentTypeMetadata BuildMaintenanceRequest()
        => new(
            TypeCode: PropertyManagementCodes.MaintenanceRequest,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_maintenance_request",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("property_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.Property)),
                        new("party_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.Party)),
                        new("category_id", ColumnType.Guid, Required: true, UiLabel: "Category", Lookup: CatalogLookup(PropertyManagementCodes.MaintenanceCategory)),
                        new("priority", ColumnType.String, Required: true),
                        new("subject", ColumnType.String, Required: true),
                        new("description", ColumnType.String),
                        new("requested_at_utc", ColumnType.Date, Required: true, UiLabel: "Requested At")
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_maintenance_request__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_maintenance_request__property_id", ["property_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_maintenance_request__party_id", ["party_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_maintenance_request__category_id", ["category_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_maintenance_request__priority", ["priority"]),
                        new DocumentIndexMetadata("ix_doc_pm_maintenance_request__requested_at_utc", ["requested_at_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Maintenance Request", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(1, "pm"),
            ListFilters:
            [
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("category_id", "Category", PropertyManagementCodes.MaintenanceCategory),
                ListOptionFilter("priority", "Priority", MaintenancePriorityOptions)
            ]);

    private static DocumentTypeMetadata BuildWorkOrder()
        => new(
            TypeCode: PropertyManagementCodes.WorkOrder,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_work_order",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("request_id", ColumnType.Guid, Required: true, UiLabel: "Request", Lookup: DocumentLookup(PropertyManagementCodes.MaintenanceRequest), MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from")),
                        new("assigned_party_id", ColumnType.Guid, UiLabel: "Assigned To", Lookup: CatalogLookup(PropertyManagementCodes.Party)),
                        new("scope_of_work", ColumnType.String, UiLabel: "Scope of Work"),
                        new("due_by_utc", ColumnType.Date, UiLabel: "Due By"),
                        new("cost_responsibility", ColumnType.String, Required: true, UiLabel: "Cost Responsibility")
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_work_order__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_work_order__request_id", ["request_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_work_order__assigned_party_id", ["assigned_party_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_work_order__due_by_utc", ["due_by_utc"]),
                        new DocumentIndexMetadata("ix_doc_pm_work_order__cost_responsibility", ["cost_responsibility"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Work Order", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(2, "pm"),
            ListFilters:
            [
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("category_id", "Category", PropertyManagementCodes.MaintenanceCategory),
                ListOptionFilter("priority", "Priority", MaintenancePriorityOptions),
                ListLookupFilter("assigned_party_id", "Assigned To", PropertyManagementCodes.Party)
            ]);

    private static DocumentTypeMetadata BuildWorkOrderCompletion()
        => new(
            TypeCode: PropertyManagementCodes.WorkOrderCompletion,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_work_order_completion",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("work_order_id", ColumnType.Guid, Required: true, UiLabel: "Work Order", Lookup: DocumentLookup(PropertyManagementCodes.WorkOrder), MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from")),
                        new("closed_at_utc", ColumnType.Date, Required: true, UiLabel: "Closed At"),
                        new("outcome", ColumnType.String, Required: true),
                        new("resolution_notes", ColumnType.String, UiLabel: "Resolution Notes")
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_work_order_completion__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_work_order_completion__work_order_id", ["work_order_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_work_order_completion__closed_at_utc", ["closed_at_utc"]),
                        new DocumentIndexMetadata("ix_doc_pm_work_order_completion__outcome", ["outcome"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Work Order Completion", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(2, "pm"),
            ListFilters:
            [
                ListOptionFilter("outcome", "Outcome", WorkOrderOutcomeOptions)
            ]);

    private static DocumentTypeMetadata BuildRentCharge()
        => new(
            TypeCode: PropertyManagementCodes.RentCharge,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_rent_charge",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("lease_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(PropertyManagementCodes.Lease)),
                        new("period_from_utc", ColumnType.Date, Required: true),
                        new("period_to_utc", ColumnType.Date, Required: true),
                        new("due_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_rent_charge__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_rent_charge__lease_id", ["lease_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_rent_charge__due_on_utc", ["due_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Rent Charge", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(2, "pm"),
            ListFilters:
            [
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party)
            ]);

    private static DocumentTypeMetadata BuildReceivableCharge()
        => new(
            TypeCode: PropertyManagementCodes.ReceivableCharge,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_receivable_charge",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("lease_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(PropertyManagementCodes.Lease)),
                        new("charge_type_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.ReceivableChargeType)),
                        new("due_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_receivable_charge__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_charge__lease_id", ["lease_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_charge__due_on_utc", ["due_on_utc"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_charge__charge_type_id", ["charge_type_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Receivable Charge", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(3, "pm"),
            ListFilters:
            [
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListLookupFilter("charge_type_id", "Charge Type", PropertyManagementCodes.ReceivableChargeType)
            ]);

    private static DocumentTypeMetadata BuildLateFeeCharge()
        => new(
            TypeCode: PropertyManagementCodes.LateFeeCharge,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_late_fee_charge",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("lease_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(PropertyManagementCodes.Lease)),
                        new("due_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_late_fee_charge__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_late_fee_charge__lease_id", ["lease_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_late_fee_charge__due_on_utc", ["due_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Late Fee Charge", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(2, "pm"),
            ListFilters:
            [
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party)
            ]);

    private static DocumentTypeMetadata BuildReceivablePayment()
        => new(
            TypeCode: PropertyManagementCodes.ReceivablePayment,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_receivable_payment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("lease_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(PropertyManagementCodes.Lease)),
                        new("bank_account_id", ColumnType.Guid, UiLabel: "Bank Account", Lookup: CatalogLookup(PropertyManagementCodes.BankAccount)),
                        new("received_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_receivable_payment__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_payment__lease_id", ["lease_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_payment__bank_account_id", ["bank_account_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_payment__received_on_utc", ["received_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Receivable Payment", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(5, "pm"),
            ListFilters:
            [
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListLookupFilter("bank_account_id", "Bank Account", PropertyManagementCodes.BankAccount)
            ]);

    private static DocumentTypeMetadata BuildReceivableReturnedPayment()
        => new(
            TypeCode: PropertyManagementCodes.ReceivableReturnedPayment,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_receivable_returned_payment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("original_payment_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(PropertyManagementCodes.ReceivablePayment)),
                        new("returned_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_receivable_returned_payment__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_returned_payment__original_payment_id", ["original_payment_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_returned_payment__returned_on_utc", ["returned_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Receivable Returned Payment", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(4, "pm"),
            ListFilters:
            [
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListLookupFilter("bank_account_id", "Bank Account", PropertyManagementCodes.BankAccount)
            ]);

    private static DocumentTypeMetadata BuildReceivableCreditMemo()
        => new(
            TypeCode: PropertyManagementCodes.ReceivableCreditMemo,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_receivable_credit_memo",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("lease_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(PropertyManagementCodes.Lease)),
                        new("charge_type_id", ColumnType.Guid, Required: true, UiLabel: "Charge Type", Lookup: CatalogLookup(PropertyManagementCodes.ReceivableChargeType)),
                        new("credited_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_receivable_credit_memo__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_credit_memo__lease_id", ["lease_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_credit_memo__charge_type_id", ["charge_type_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_credit_memo__credited_on_utc", ["credited_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Receivable Credit Memo", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(5, "pm"),
            ListFilters:
            [
                ListDocumentFilter("lease_id", "Lease", PropertyManagementCodes.Lease),
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListLookupFilter("charge_type_id", "Charge Type", PropertyManagementCodes.ReceivableChargeType)
            ]);

    private static DocumentTypeMetadata BuildPayableCharge()
        => new(
            TypeCode: PropertyManagementCodes.PayableCharge,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_payable_charge",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("party_id", ColumnType.Guid, Required: true, UiLabel: "Vendor", Lookup: CatalogLookup(PropertyManagementCodes.Party)),
                        new("property_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.Property)),
                        new("charge_type_id", ColumnType.Guid, Required: true, UiLabel: "Charge Type", Lookup: CatalogLookup(PropertyManagementCodes.PayableChargeType)),
                        new("vendor_invoice_no", ColumnType.String, UiLabel: "Vendor Invoice No"),
                        new("due_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_payable_charge__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_charge__party_id", ["party_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_charge__property_id", ["property_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_charge__charge_type_id", ["charge_type_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_charge__due_on_utc", ["due_on_utc"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_charge__vendor_invoice_no", ["vendor_invoice_no"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Payable Charge", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "pm"),
            ListFilters:
            [
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListLookupFilter("charge_type_id", "Charge Type", PropertyManagementCodes.PayableChargeType)
            ]);

    private static DocumentTypeMetadata BuildPayablePayment()
        => new(
            TypeCode: PropertyManagementCodes.PayablePayment,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_payable_payment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("party_id", ColumnType.Guid, Required: true, UiLabel: "Vendor", Lookup: CatalogLookup(PropertyManagementCodes.Party)),
                        new("property_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.Property)),
                        new("bank_account_id", ColumnType.Guid, UiLabel: "Bank Account", Lookup: CatalogLookup(PropertyManagementCodes.BankAccount)),
                        new("paid_on_utc", ColumnType.Date, Required: true, UiLabel: "Paid On"),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_payable_payment__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_payment__party_id", ["party_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_payment__property_id", ["property_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_payment__bank_account_id", ["bank_account_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_payment__paid_on_utc", ["paid_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Payable Payment", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "pm"),
            ListFilters:
            [
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListLookupFilter("bank_account_id", "Bank Account", PropertyManagementCodes.BankAccount)
            ]);

    private static DocumentTypeMetadata BuildPayableCreditMemo()
        => new(
            TypeCode: PropertyManagementCodes.PayableCreditMemo,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_payable_credit_memo",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("party_id", ColumnType.Guid, Required: true, UiLabel: "Vendor", Lookup: CatalogLookup(PropertyManagementCodes.Party)),
                        new("property_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(PropertyManagementCodes.Property)),
                        new("charge_type_id", ColumnType.Guid, Required: true, UiLabel: "Charge Type", Lookup: CatalogLookup(PropertyManagementCodes.PayableChargeType)),
                        new("credited_on_utc", ColumnType.Date, Required: true, UiLabel: "Credited On"),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_payable_credit_memo__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_credit_memo__party_id", ["party_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_credit_memo__property_id", ["property_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_credit_memo__charge_type_id", ["charge_type_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_credit_memo__credited_on_utc", ["credited_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Payable Credit Memo", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(2, "pm"),
            ListFilters:
            [
                ListLookupFilter("property_id", "Property", PropertyManagementCodes.Property),
                ListLookupFilter("party_id", "Party", PropertyManagementCodes.Party),
                ListLookupFilter("charge_type_id", "Charge Type", PropertyManagementCodes.PayableChargeType)
            ]);

    private static DocumentTypeMetadata BuildPayableApply()
        => new(
            TypeCode: PropertyManagementCodes.PayableApply,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_payable_apply",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("credit_document_id", ColumnType.Guid, Required: true, UiLabel: "Credit Source", Lookup: DocumentLookup(PropertyManagementCodes.PayablePayment, PropertyManagementCodes.PayableCreditMemo)),
                        new("charge_document_id", ColumnType.Guid, Required: true, UiLabel: "Charge", Lookup: DocumentLookup(PropertyManagementCodes.PayableCharge)),
                        new("applied_on_utc", ColumnType.Date, Required: true, UiLabel: "Applied On"),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_payable_apply__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_apply__credit_document_id", ["credit_document_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_apply__charge_document_id", ["charge_document_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_payable_apply__applied_on_utc", ["applied_on_utc"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Payable Apply", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "pm"),
            ListFilters:
            [
                ListAmountFilter("amount")
            ]);

    private static DocumentTypeMetadata BuildReceivableApply()
        => new(
            TypeCode: PropertyManagementCodes.ReceivableApply,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_pm_receivable_apply",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("credit_document_id", ColumnType.Guid, Required: true, UiLabel: "Credit Source", Lookup: DocumentLookup(PropertyManagementCodes.ReceivablePayment, PropertyManagementCodes.ReceivableCreditMemo)),
                        new("charge_document_id", ColumnType.Guid, Required: true, UiLabel: "Charge", Lookup: DocumentLookup(PropertyManagementCodes.ReceivableCharge, PropertyManagementCodes.LateFeeCharge, PropertyManagementCodes.RentCharge)),
                        new("applied_on_utc", ColumnType.Date, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_pm_receivable_apply__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_apply__credit_document_id", ["credit_document_id"]),
                        new DocumentIndexMetadata("ix_doc_pm_receivable_apply__charge_document_id", ["charge_document_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Receivable Apply", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(3, "pm"),
            ListFilters:
            [
                ListAmountFilter("amount")
            ]);
}
