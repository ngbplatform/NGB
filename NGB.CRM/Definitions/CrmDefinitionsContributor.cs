using NGB.CRM.Documents.Numbering;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;

namespace NGB.CRM.Definitions;

/// <summary>
/// CRM metadata definitions for a package-consumer industry demo.
/// </summary>
public sealed class CrmDefinitionsContributor : IDefinitionsContributor
{
    private static readonly FieldOptionMetadata[] AccountTypeOptions =
    [
        new("Prospect", "Prospect"),
        new("Customer", "Customer"),
        new("Partner", "Partner"),
        new("Vendor", "Vendor"),
        new("Other", "Other")
    ];

    private static readonly FieldOptionMetadata[] QualificationStateOptions =
    [
        new("New", "New"),
        new("Qualified", "Qualified"),
        new("Disqualified", "Disqualified"),
        new("Converted", "Converted")
    ];

    private static readonly FieldOptionMetadata[] OpportunityStatusOptions =
    [
        new("Open", "Open"),
        new("Won", "Won"),
        new("Lost", "Lost")
    ];

    private static readonly FieldOptionMetadata[] QuoteStatusOptions =
    [
        new("Draft", "Draft"),
        new("Presented", "Presented"),
        new("Accepted", "Accepted"),
        new("Rejected", "Rejected"),
        new("Expired", "Expired")
    ];

    private static readonly FieldOptionMetadata[] ActivityTypeOptions =
    [
        new("Call", "Call"),
        new("Email", "Email"),
        new("Meeting", "Meeting"),
        new("Task", "Task"),
        new("Note", "Note")
    ];

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocumentRelationshipType("qualifies", r => r
            .Name("Qualifies")
            .ManyToOne()
            .AllowFromDocumentTypes(CrmCodes.LeadQualification)
            .AllowToDocumentTypes(CrmCodes.LeadIntake));
        builder.AddDocumentRelationshipType("converts", r => r
            .Name("Converts")
            .ManyToOne()
            .AllowFromDocumentTypes(CrmCodes.LeadConversion)
            .AllowToDocumentTypes(CrmCodes.LeadIntake));
        builder.AddDocumentRelationshipType("updates", r => r
            .Name("Updates")
            .ManyToOne()
            .AllowFromDocumentTypes(CrmCodes.OpportunityUpdate)
            .AllowToDocumentTypes(CrmCodes.LeadConversion));
        builder.AddDocumentRelationshipType("quotes", r => r
            .Name("Quotes")
            .ManyToOne()
            .AllowFromDocumentTypes(CrmCodes.Quote)
            .AllowToDocumentTypes(CrmCodes.LeadConversion));
        builder.AddDocumentRelationshipType("activity_for_lead", r => r
            .Name("Activity For Lead")
            .ManyToOne()
            .AllowFromDocumentTypes(CrmCodes.ActivityLog)
            .AllowToDocumentTypes(CrmCodes.LeadIntake));
        builder.AddDocumentRelationshipType("activity_for_opportunity", r => r
            .Name("Activity For Opportunity")
            .ManyToOne()
            .AllowFromDocumentTypes(CrmCodes.ActivityLog)
            .AllowToDocumentTypes(CrmCodes.LeadConversion));

        builder.AddCatalog(CrmCodes.Account, c => c.Metadata(BuildAccount()));
        builder.AddCatalog(CrmCodes.Contact, c => c.Metadata(BuildContact()));
        builder.AddCatalog(CrmCodes.Product, c => c.Metadata(BuildProduct()));
        builder.AddCatalog(CrmCodes.OpportunityStage, c => c.Metadata(BuildOpportunityStage()));

        builder.AddDocument(CrmCodes.LeadIntake, d => d
            .Metadata(BuildLeadIntake())
            .NumberingPolicy<CrmLeadIntakeNumberingPolicy>());
        builder.AddDocument(CrmCodes.LeadQualification, d => d
            .Metadata(BuildLeadQualification())
            .NumberingPolicy<CrmLeadQualificationNumberingPolicy>());
        builder.AddDocument(CrmCodes.LeadConversion, d => d
            .Metadata(BuildLeadConversion())
            .NumberingPolicy<CrmLeadConversionNumberingPolicy>());
        builder.AddDocument(CrmCodes.OpportunityUpdate, d => d
            .Metadata(BuildOpportunityUpdate())
            .NumberingPolicy<CrmOpportunityUpdateNumberingPolicy>());
        builder.AddDocument(CrmCodes.Quote, d => d
            .Metadata(BuildQuote())
            .NumberingPolicy<CrmQuoteNumberingPolicy>());
        builder.AddDocument(CrmCodes.ActivityLog, d => d
            .Metadata(BuildActivityLog())
            .NumberingPolicy<CrmActivityLogNumberingPolicy>());
    }

    private static CatalogLookupSourceMetadata CatalogLookup(string catalogType) => new(catalogType);

    private static DocumentLookupSourceMetadata DocumentLookup(params string[] documentTypes) => new(documentTypes);

    private static DocumentListFilterMetadata ListLookupFilter(string key, string label, string catalogType)
        => new(Key: key, Label: label, Type: ColumnType.Guid, IsMulti: true, Lookup: CatalogLookup(catalogType));

    private static DocumentListFilterMetadata ListDocumentFilter(string key, string label, params string[] documentTypes)
        => new(Key: key, Label: label, Type: ColumnType.Guid, IsMulti: true, Lookup: DocumentLookup(documentTypes));

    private static DocumentListFilterMetadata ListOptionFilter(string key, string label, params FieldOptionMetadata[] options)
        => new(
            Key: key,
            Label: label,
            Type: ColumnType.String,
            IsMulti: true,
            Options: options.Select(static x => new DocumentListFilterOptionMetadata(x.Value, x.Label)).ToArray());

    private static CatalogTypeMetadata BuildAccount()
        => new(
            CatalogCode: CrmCodes.Account,
            DisplayName: "Account",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_crm_account",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("account_number", ColumnType.String),
                        new("name", ColumnType.String, Required: true),
                        new("legal_name", ColumnType.String),
                        new("account_type", ColumnType.String, Required: true, Options: AccountTypeOptions),
                        new("industry", ColumnType.String),
                        new("website", ColumnType.String),
                        new("phone", ColumnType.String),
                        new("email", ColumnType.String),
                        new("billing_address", ColumnType.String),
                        new("owner_user_id", ColumnType.Guid),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_crm_account__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_crm_account__account_number", ["account_number"]),
                        new CatalogIndexMetadata("ix_cat_crm_account__name", ["name"]),
                        new CatalogIndexMetadata("ix_cat_crm_account__account_type", ["account_type"]),
                        new CatalogIndexMetadata("ix_cat_crm_account__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_crm_account", "display"),
            Version: new CatalogMetadataVersion(1, "crm"));

    private static CatalogTypeMetadata BuildContact()
        => new(
            CatalogCode: CrmCodes.Contact,
            DisplayName: "Contact",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_crm_contact",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("account_id", ColumnType.Guid, Lookup: CatalogLookup(CrmCodes.Account)),
                        new("first_name", ColumnType.String, Required: true),
                        new("last_name", ColumnType.String, Required: true),
                        new("title", ColumnType.String),
                        new("email", ColumnType.String),
                        new("phone", ColumnType.String),
                        new("mobile_phone", ColumnType.String),
                        new("is_primary", ColumnType.Boolean, Required: true),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_crm_contact__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_crm_contact__account_id", ["account_id"]),
                        new CatalogIndexMetadata("ix_cat_crm_contact__email", ["email"]),
                        new CatalogIndexMetadata("ix_cat_crm_contact__last_name", ["last_name"]),
                        new CatalogIndexMetadata("ix_cat_crm_contact__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_crm_contact", "display"),
            Version: new CatalogMetadataVersion(1, "crm"));

    private static CatalogTypeMetadata BuildProduct()
        => new(
            CatalogCode: CrmCodes.Product,
            DisplayName: "Product",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_crm_product",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("sku", ColumnType.String),
                        new("name", ColumnType.String, Required: true),
                        new("family", ColumnType.String),
                        new("unit_of_measure", ColumnType.String),
                        new("list_price", ColumnType.Decimal),
                        new("currency", ColumnType.String),
                        new("is_active", ColumnType.Boolean, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_crm_product__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_crm_product__sku", ["sku"]),
                        new CatalogIndexMetadata("ix_cat_crm_product__family", ["family"]),
                        new CatalogIndexMetadata("ix_cat_crm_product__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_crm_product", "display"),
            Version: new CatalogMetadataVersion(1, "crm"));

    private static CatalogTypeMetadata BuildOpportunityStage()
        => new(
            CatalogCode: CrmCodes.OpportunityStage,
            DisplayName: "Opportunity Stage",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_crm_opportunity_stage",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("stage_code", ColumnType.String, Required: true),
                        new("name", ColumnType.String, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("default_probability", ColumnType.Decimal, Required: true),
                        new("is_closed", ColumnType.Boolean, Required: true),
                        new("is_won", ColumnType.Boolean, Required: true),
                        new("is_active", ColumnType.Boolean, Required: true)
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_crm_opportunity_stage__display", ["display"]),
                        new CatalogIndexMetadata("ix_cat_crm_opportunity_stage__stage_code", ["stage_code"]),
                        new CatalogIndexMetadata("ix_cat_crm_opportunity_stage__ordinal", ["ordinal"]),
                        new CatalogIndexMetadata("ix_cat_crm_opportunity_stage__is_active", ["is_active"])
                    ])
            ],
            Presentation: new CatalogPresentationMetadata("cat_crm_opportunity_stage", "display"),
            Version: new CatalogMetadataVersion(1, "crm"));

    private static DocumentTypeMetadata BuildLeadIntake()
        => new(
            TypeCode: CrmCodes.LeadIntake,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_crm_lead_intake",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("lead_name", ColumnType.String, Required: true),
                        new("company_name", ColumnType.String),
                        new("contact_name", ColumnType.String, Required: true),
                        new("email", ColumnType.String),
                        new("phone", ColumnType.String),
                        new("lead_source", ColumnType.String),
                        new("industry", ColumnType.String),
                        new("estimated_value", ColumnType.Decimal),
                        new("currency", ColumnType.String),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_crm_lead_intake__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_intake__document_date_utc", ["document_date_utc"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_intake__email", ["email"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_intake__company_name", ["company_name"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Lead Intake", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "estimated_value"),
            Version: new DocumentMetadataVersion(1, "crm"));

    private static DocumentTypeMetadata BuildLeadQualification()
        => new(
            TypeCode: CrmCodes.LeadQualification,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_crm_lead_qualification",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("lead_intake_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(CrmCodes.LeadIntake), MirroredRelationship: new MirroredDocumentRelationshipMetadata("qualifies")),
                        new("qualification_state", ColumnType.String, Required: true, Options: QualificationStateOptions),
                        new("score", ColumnType.Int32, Required: true),
                        new("disqualification_reason", ColumnType.String),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_crm_lead_qualification__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_qualification__lead_intake_id", ["lead_intake_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_qualification__qualification_state", ["qualification_state"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Lead Qualification", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(1, "crm"),
            ListFilters:
            [
                ListDocumentFilter("lead_intake_id", "Lead", CrmCodes.LeadIntake),
                ListOptionFilter("qualification_state", "Qualification State", QualificationStateOptions)
            ]);

    private static DocumentTypeMetadata BuildLeadConversion()
        => new(
            TypeCode: CrmCodes.LeadConversion,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_crm_lead_conversion",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("lead_intake_id", ColumnType.Guid, Required: true, Lookup: DocumentLookup(CrmCodes.LeadIntake), MirroredRelationship: new MirroredDocumentRelationshipMetadata("converts")),
                        // A conversion derived from a qualified lead is persisted before these values are selected.
                        // LeadConversionPostValidator enforces both fields at the Draft -> Posted boundary.
                        new("account_id", ColumnType.Guid, Lookup: CatalogLookup(CrmCodes.Account)),
                        new("contact_id", ColumnType.Guid, Lookup: CatalogLookup(CrmCodes.Contact)),
                        new("create_opportunity", ColumnType.Boolean, Required: true),
                        new("opportunity_name", ColumnType.String),
                        new("stage_id", ColumnType.Guid, Lookup: CatalogLookup(CrmCodes.OpportunityStage)),
                        new("amount", ColumnType.Decimal),
                        new("probability", ColumnType.Decimal),
                        new("expected_close_date", ColumnType.Date),
                        new("currency", ColumnType.String),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_crm_lead_conversion__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_conversion__lead_intake_id", ["lead_intake_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_conversion__account_id", ["account_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_conversion__contact_id", ["contact_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_lead_conversion__stage_id", ["stage_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Lead Conversion", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "crm"),
            ListFilters:
            [
                ListDocumentFilter("lead_intake_id", "Lead", CrmCodes.LeadIntake),
                ListLookupFilter("account_id", "Account", CrmCodes.Account),
                ListLookupFilter("contact_id", "Contact", CrmCodes.Contact),
                ListLookupFilter("stage_id", "Stage", CrmCodes.OpportunityStage)
            ]);

    private static DocumentTypeMetadata BuildOpportunityUpdate()
        => new(
            TypeCode: CrmCodes.OpportunityUpdate,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_crm_opportunity_update",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("opportunity_id", ColumnType.Guid, Required: true, UiLabel: "Opportunity", Lookup: DocumentLookup(CrmCodes.LeadConversion), MirroredRelationship: new MirroredDocumentRelationshipMetadata("updates")),
                        new("stage_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(CrmCodes.OpportunityStage)),
                        new("amount", ColumnType.Decimal),
                        new("probability", ColumnType.Decimal, Required: true),
                        new("expected_close_date", ColumnType.Date),
                        new("status", ColumnType.String, Required: true, Options: OpportunityStatusOptions),
                        new("loss_reason", ColumnType.String),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_crm_opportunity_update__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_crm_opportunity_update__opportunity_id", ["opportunity_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_opportunity_update__stage_id", ["stage_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_opportunity_update__status", ["status"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Opportunity Update", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "crm"),
            ListFilters:
            [
                ListDocumentFilter("opportunity_id", "Opportunity", CrmCodes.LeadConversion),
                ListLookupFilter("stage_id", "Stage", CrmCodes.OpportunityStage),
                ListOptionFilter("status", "Status", OpportunityStatusOptions)
            ]);

    private static DocumentTypeMetadata BuildQuote()
        => new(
            TypeCode: CrmCodes.Quote,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_crm_quote",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("opportunity_id", ColumnType.Guid, Required: true, UiLabel: "Opportunity", Lookup: DocumentLookup(CrmCodes.LeadConversion), MirroredRelationship: new MirroredDocumentRelationshipMetadata("quotes")),
                        new("account_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(CrmCodes.Account)),
                        new("contact_id", ColumnType.Guid, Lookup: CatalogLookup(CrmCodes.Contact)),
                        new("valid_until", ColumnType.Date, Required: true),
                        new("currency", ColumnType.String, Required: true),
                        new("quote_status", ColumnType.String, Required: true, Options: QuoteStatusOptions),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_crm_quote__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_crm_quote__opportunity_id", ["opportunity_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_quote__account_id", ["account_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_quote__quote_status", ["quote_status"])
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_crm_quote__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("product_id", ColumnType.Guid, Required: true, Lookup: CatalogLookup(CrmCodes.Product)),
                        new("description", ColumnType.String),
                        new("quantity", ColumnType.Decimal, Required: true),
                        new("unit_price", ColumnType.Decimal, Required: true),
                        new("discount_percent", ColumnType.Decimal, Required: true),
                        new("line_amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_crm_quote__lines__document_id", ["document_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_quote__lines__product_id", ["product_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Quote", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true, AmountField: "amount"),
            Version: new DocumentMetadataVersion(1, "crm"),
            ListFilters:
            [
                ListDocumentFilter("opportunity_id", "Opportunity", CrmCodes.LeadConversion),
                ListLookupFilter("account_id", "Account", CrmCodes.Account),
                ListOptionFilter("quote_status", "Quote Status", QuoteStatusOptions)
            ]);

    private static DocumentTypeMetadata BuildActivityLog()
        => new(
            TypeCode: CrmCodes.ActivityLog,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_crm_activity_log",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date, Required: true),
                        new("activity_type", ColumnType.String, Required: true, Options: ActivityTypeOptions),
                        new("subject", ColumnType.String, Required: true),
                        new("lead_intake_id", ColumnType.Guid, Lookup: DocumentLookup(CrmCodes.LeadIntake), MirroredRelationship: new MirroredDocumentRelationshipMetadata("activity_for_lead")),
                        new("account_id", ColumnType.Guid, Lookup: CatalogLookup(CrmCodes.Account)),
                        new("contact_id", ColumnType.Guid, Lookup: CatalogLookup(CrmCodes.Contact)),
                        new("opportunity_id", ColumnType.Guid, Lookup: DocumentLookup(CrmCodes.LeadConversion), MirroredRelationship: new MirroredDocumentRelationshipMetadata("activity_for_opportunity")),
                        new("due_at_utc", ColumnType.DateTimeUtc),
                        new("completed_at_utc", ColumnType.DateTimeUtc),
                        new("outcome", ColumnType.String),
                        new("notes", ColumnType.String)
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_crm_activity_log__display", ["display"]),
                        new DocumentIndexMetadata("ix_doc_crm_activity_log__activity_type", ["activity_type"]),
                        new DocumentIndexMetadata("ix_doc_crm_activity_log__lead_intake_id", ["lead_intake_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_activity_log__account_id", ["account_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_activity_log__contact_id", ["contact_id"]),
                        new DocumentIndexMetadata("ix_doc_crm_activity_log__opportunity_id", ["opportunity_id"])
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Activity Log", HasNumber: true, ComputedDisplay: true, HideSystemFieldsInEditor: true),
            Version: new DocumentMetadataVersion(1, "crm"),
            ListFilters:
            [
                ListOptionFilter("activity_type", "Activity Type", ActivityTypeOptions),
                ListDocumentFilter("lead_intake_id", "Lead", CrmCodes.LeadIntake),
                ListLookupFilter("account_id", "Account", CrmCodes.Account),
                ListLookupFilter("contact_id", "Contact", CrmCodes.Contact),
                ListDocumentFilter("opportunity_id", "Opportunity", CrmCodes.LeadConversion)
            ]);
}
