using NGB.AgencyBilling.Documents.Numbering;
using NGB.AgencyBilling.Enums;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;

namespace NGB.AgencyBilling.Definitions;

/// <summary>
/// Agency Billing metadata definitions for the production-ready phase-1 slice.
/// </summary>
public sealed class AgencyBillingDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddCatalog(AgencyBillingCodes.Client, c => c.Metadata(BuildClient()));
        builder.AddCatalog(AgencyBillingCodes.TeamMember, c => c.Metadata(BuildTeamMember()));
        builder.AddCatalog(AgencyBillingCodes.Project, c => c.Metadata(BuildProject()));
        builder.AddCatalog(AgencyBillingCodes.RateCard, c => c.Metadata(BuildRateCard()));
        builder.AddCatalog(AgencyBillingCodes.ServiceItem, c => c.Metadata(BuildServiceItem()));
        builder.AddCatalog(AgencyBillingCodes.PaymentTerms, c => c.Metadata(BuildPaymentTerms()));
        builder.AddCatalog(AgencyBillingCodes.AccountingPolicy, c => c.Metadata(BuildAccountingPolicy()));

        builder.AddDocument(AgencyBillingCodes.ClientContract, d => d
            .Metadata(BuildClientContract())
            .NumberingPolicy<AgencyBillingClientContractNumberingPolicy>());
        builder.AddDocument(AgencyBillingCodes.Timesheet, d => d
            .Metadata(BuildTimesheet())
            .NumberingPolicy<AgencyBillingTimesheetNumberingPolicy>());
        builder.AddDocument(AgencyBillingCodes.SalesInvoice, d => d
            .Metadata(BuildSalesInvoice())
            .NumberingPolicy<AgencyBillingSalesInvoiceNumberingPolicy>());
        builder.AddDocument(AgencyBillingCodes.CustomerPayment, d => d
            .Metadata(BuildCustomerPayment())
            .NumberingPolicy<AgencyBillingCustomerPaymentNumberingPolicy>());
    }

    private static CatalogLookupSourceMetadata CatalogLookup(string catalogType) => new(catalogType);
    private static DocumentLookupSourceMetadata DocumentLookup(params string[] documentTypes) => new(documentTypes);
    private static ChartOfAccountsLookupSourceMetadata CoaLookup() => new();
    private static IReadOnlyList<FieldOptionMetadata> EnumOptions<TEnum>()
        where TEnum : struct, Enum
        => FieldOptionMetadataTools.EnumOptions<TEnum>();

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

    private static CatalogTypeMetadata BuildClient()
        => new(
            CatalogCode: AgencyBillingCodes.Client,
            DisplayName: "Client",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_ab_client",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("client_code", ColumnType.String),
                        new("name", ColumnType.String, Required: true),
                        new("legal_name", ColumnType.String),
                        new("status", ColumnType.Int32, Required: true, Options: EnumOptions<AgencyBillingClientStatus>()),
                        new("email", ColumnType.String),
                        new("phone", ColumnType.String),
                        new("billing_contact", ColumnType.String),
                        new("payment_terms_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.PaymentTerms)),
                        new("default_currency", ColumnType.String),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_ab_client__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_ab_client__client_code", ["client_code"]),
                        new CatalogIndexMetadata("ix_cat_ab_client__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_ab_client__status", ["status"]),
                        new CatalogIndexMetadata("ix_cat_ab_client__payment_terms_id", ["payment_terms_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_client__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_ab_client", "display"),
            Version: new CatalogMetadataVersion(1, "ab"));

    private static CatalogTypeMetadata BuildTeamMember()
        => new(
            CatalogCode: AgencyBillingCodes.TeamMember,
            DisplayName: "Team Member",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_ab_team_member",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("member_code", ColumnType.String),
                        new("full_name", ColumnType.String, Required: true),
                        new("member_type", ColumnType.Int32, Required: true, Options: EnumOptions<AgencyBillingTeamMemberType>()),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("billable_by_default", ColumnType.Boolean, Required: true),
                        new("default_billing_rate", ColumnType.Decimal),
                        new("default_cost_rate", ColumnType.Decimal),
                        new("email", ColumnType.String),
                        new("title", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_ab_team_member__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_ab_team_member__member_code", ["member_code"]),
                        new CatalogIndexMetadata("ix_cat_ab_team_member__full_name", ["full_name"]),
                        new CatalogIndexMetadata("ix_cat_ab_team_member__member_type", ["member_type"]),
                        new CatalogIndexMetadata("ix_cat_ab_team_member__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_ab_team_member", "display"),
            Version: new CatalogMetadataVersion(1, "ab"));

    private static CatalogTypeMetadata BuildProject()
        => new(
            CatalogCode: AgencyBillingCodes.Project,
            DisplayName: "Project",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_ab_project",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("project_code", ColumnType.String),
                        new("name", ColumnType.String, Required: true),
                        new("client_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Client)),
                        new("project_manager_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.TeamMember)),
                        new("start_date", ColumnType.Date),
                        new("end_date", ColumnType.Date),
                        new("status", ColumnType.Int32, Required: true, Options: EnumOptions<AgencyBillingProjectStatus>()),
                        new("billing_model", ColumnType.Int32, Required: true, Options: EnumOptions<AgencyBillingProjectBillingModel>()),
                        new("budget_hours", ColumnType.Decimal),
                        new("budget_amount", ColumnType.Decimal),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_ab_project__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_ab_project__project_code", ["project_code"]),
                        new CatalogIndexMetadata("ix_cat_ab_project__client_id", ["client_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_project__project_manager_id", ["project_manager_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_project__status", ["status"]),
                        new CatalogIndexMetadata("ix_cat_ab_project__billing_model", ["billing_model"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_ab_project", "display"),
            Version: new CatalogMetadataVersion(1, "ab"));

    private static CatalogTypeMetadata BuildRateCard()
        => new(
            CatalogCode: AgencyBillingCodes.RateCard,
            DisplayName: "Rate Card",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_ab_rate_card",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("client_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.Client)),
                        new("project_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.Project)),
                        new("team_member_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.TeamMember)),
                        new("service_item_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.ServiceItem)),
                        new("service_title", ColumnType.String),
                        new("billing_rate", ColumnType.Decimal, Required: true),
                        new("cost_rate", ColumnType.Decimal),
                        new("effective_from", ColumnType.Date),
                        new("effective_to", ColumnType.Date),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_ab_rate_card__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_ab_rate_card__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_ab_rate_card__client_id", ["client_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_rate_card__project_id", ["project_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_rate_card__team_member_id", ["team_member_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_rate_card__service_item_id", ["service_item_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_rate_card__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_ab_rate_card", "display"),
            Version: new CatalogMetadataVersion(1, "ab"));

    private static CatalogTypeMetadata BuildServiceItem()
        => new(
            CatalogCode: AgencyBillingCodes.ServiceItem,
            DisplayName: "Service Item",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_ab_service_item",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("code", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("unit_of_measure", ColumnType.Int32, Options: EnumOptions<AgencyBillingServiceItemUnitOfMeasure>()),
                        new("default_revenue_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_ab_service_item__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_ab_service_item__code", ["code"]),
                        new CatalogIndexMetadata("ix_cat_ab_service_item__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_ab_service_item__default_revenue_account_id", ["default_revenue_account_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_service_item__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_ab_service_item", "display"),
            Version: new CatalogMetadataVersion(1, "ab"));

    private static CatalogTypeMetadata BuildPaymentTerms()
        => new(
            CatalogCode: AgencyBillingCodes.PaymentTerms,
            DisplayName: "Payment Terms",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_ab_payment_terms",
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
                        new CatalogIndexMetadata("ix_cat_ab_payment_terms__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_ab_payment_terms__code", ["code"]),
                        new CatalogIndexMetadata("ix_cat_ab_payment_terms__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_ab_payment_terms__due_days", ["due_days"]),
                        new CatalogIndexMetadata("ix_cat_ab_payment_terms__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_ab_payment_terms", "display"),
            Version: new CatalogMetadataVersion(1, "ab"));

    private static CatalogTypeMetadata BuildAccountingPolicy()
        => new(
            CatalogCode: AgencyBillingCodes.AccountingPolicy,
            DisplayName: "Accounting Policy",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_ab_accounting_policy",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("cash_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("ar_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("service_revenue_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("project_time_ledger_register_id", ColumnType.Guid),
                        new("unbilled_time_register_id", ColumnType.Guid),
                        new("project_billing_status_register_id", ColumnType.Guid),
                        new("ar_open_items_register_id", ColumnType.Guid),
                        new("default_currency", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_ab_accounting_policy__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_ab_accounting_policy__cash_account_id", ["cash_account_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_accounting_policy__ar_account_id", ["ar_account_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_accounting_policy__service_revenue_account_id", ["service_revenue_account_id"]),
                        new CatalogIndexMetadata("ix_cat_ab_accounting_policy__ar_open_items_register_id", ["ar_open_items_register_id"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_ab_accounting_policy", "display"),
            Version: new CatalogMetadataVersion(1, "ab"));

    private static DocumentTypeMetadata BuildClientContract()
        => new(
            TypeCode: AgencyBillingCodes.ClientContract,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_ab_client_contract",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("effective_from", ColumnType.Date, Required: true),
                        new("effective_to", ColumnType.Date),
                        new("client_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Client)),
                        new("project_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Project)),
                        new("currency_code", ColumnType.String, Required: true),
                        new("billing_frequency", ColumnType.Int32, Required: true, Options: EnumOptions<AgencyBillingContractBillingFrequency>()),
                        new("payment_terms_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.PaymentTerms)),
                        new("invoice_memo_template", ColumnType.String),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__effective_from", ["effective_from"]),
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__client_id", ["client_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__project_id", ["project_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__payment_terms_id", ["payment_terms_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_ab_client_contract__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("service_item_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.ServiceItem)),
                        new("team_member_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.TeamMember)),
                        new("service_title", ColumnType.String),
                        new("billing_rate", ColumnType.Decimal, Required: true),
                        new("cost_rate", ColumnType.Decimal),
                        new("active_from", ColumnType.Date),
                        new("active_to", ColumnType.Date),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__lines__service_item_id", ["service_item_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_client_contract__lines__team_member_id", ["team_member_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Client Contract", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(1, "ab"),
            ListFilters:
            [
                ListLookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                ListLookupFilter("project_id", "Project", AgencyBillingCodes.Project),
                ListLookupFilter("payment_terms_id", "Payment Terms", AgencyBillingCodes.PaymentTerms)
            ]);

    private static DocumentTypeMetadata BuildTimesheet()
        => new(
            TypeCode: AgencyBillingCodes.Timesheet,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_ab_timesheet",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("team_member_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.TeamMember)),
                        new("project_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Project)),
                        new("client_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Client)),
                        new("work_date", ColumnType.Date, Required: true),
                        new("total_hours", ColumnType.Decimal),
                        new("amount", ColumnType.Decimal),
                        new("cost_amount", ColumnType.Decimal),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__team_member_id", ["team_member_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__project_id", ["project_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__client_id", ["client_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__work_date", ["work_date"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_ab_timesheet__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("service_item_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.ServiceItem)),
                        new("description", ColumnType.String),
                        new("hours", ColumnType.Decimal, Required: true),
                        new("billable", ColumnType.Boolean, Required: true),
                        new("billing_rate", ColumnType.Decimal),
                        new("cost_rate", ColumnType.Decimal),
                        new("line_amount", ColumnType.Decimal),
                        new("line_cost_amount", ColumnType.Decimal)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_timesheet__lines__service_item_id", ["service_item_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Timesheet", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "ab"),
            ListFilters:
            [
                ListLookupFilter("team_member_id", "Team Member", AgencyBillingCodes.TeamMember),
                ListLookupFilter("project_id", "Project", AgencyBillingCodes.Project),
                ListLookupFilter("client_id", "Client", AgencyBillingCodes.Client)
            ]);

    private static DocumentTypeMetadata BuildSalesInvoice()
        => new(
            TypeCode: AgencyBillingCodes.SalesInvoice,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_ab_sales_invoice",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("due_date", ColumnType.Date, Required: true),
                        new("client_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Client)),
                        new("project_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Project)),
                        new("contract_id", ColumnType.Guid, Lookup: DocumentLookup(AgencyBillingCodes.ClientContract), MirroredRelationship: new MirroredDocumentRelationshipMetadata("based_on")),
                        new("currency_code", ColumnType.String, Required: true),
                        new("memo", ColumnType.String),
                        new("amount", ColumnType.Decimal),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__due_date", ["due_date"]),
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__client_id", ["client_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__project_id", ["project_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__contract_id", ["contract_id"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_ab_sales_invoice__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("service_item_id", ColumnType.Guid, Lookup: CatalogLookup(AgencyBillingCodes.ServiceItem)),
                        new("source_timesheet_id", ColumnType.Guid, Lookup: DocumentLookup(AgencyBillingCodes.Timesheet)),
                        new("description", ColumnType.String, Required: true),
                        new("quantity_hours", ColumnType.Decimal, Required: true),
                        new("rate", ColumnType.Decimal, Required: true),
                        new("line_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__lines__service_item_id", ["service_item_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_sales_invoice__lines__source_timesheet_id", ["source_timesheet_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Sales Invoice", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "ab"),
            ListFilters:
            [
                ListLookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                ListLookupFilter("project_id", "Project", AgencyBillingCodes.Project),
                ListDocumentFilter("contract_id", "Contract", AgencyBillingCodes.ClientContract)
            ]);

    private static DocumentTypeMetadata BuildCustomerPayment()
        => new(
            TypeCode: AgencyBillingCodes.CustomerPayment,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_ab_customer_payment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("client_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(AgencyBillingCodes.Client)),
                        new("cash_account_id", ColumnType.Guid, Lookup: CoaLookup()),
                        new("reference_number", ColumnType.String),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_customer_payment__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_ab_customer_payment__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_ab_customer_payment__client_id", ["client_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_customer_payment__cash_account_id", ["cash_account_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_customer_payment__reference_number", ["reference_number"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_ab_customer_payment__applies",
                    Kind: TableKind.Part,
                    PartCode: "applies",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("sales_invoice_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(AgencyBillingCodes.SalesInvoice)),
                        new("applied_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_ab_customer_payment__applies__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_ab_customer_payment__applies__sales_invoice_id", ["sales_invoice_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Customer Payment", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "ab"),
            ListFilters:
            [
                ListLookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                ListCoaFilter("cash_account_id", "Cash / Bank Account")
            ]);
}
