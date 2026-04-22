using FluentAssertions;
using NGB.AgencyBilling.Definitions;
using NGB.AgencyBilling.Documents.Numbering;
using NGB.AgencyBilling.Runtime.Catalogs.Validation;
using NGB.AgencyBilling.Runtime.Derivations;
using NGB.AgencyBilling.Runtime.Documents.Validation;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Reporting;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;

namespace NGB.AgencyBilling.Runtime.Tests.Metadata;

public sealed class AgencyBillingDefinitionsContributor_P0Tests
{
    private static readonly DefinitionsRegistry Registry = BuildRegistry();

    [Theory]
    [MemberData(nameof(CatalogDefinitionCases))]
    public void Catalog_Definitions_Are_Registered_With_Expected_Presentation_And_Version(
        string typeCode,
        string displayName,
        string headTable,
        string displayField)
    {
        var definition = Registry.GetCatalog(typeCode);
        var metadata = definition.Metadata;

        definition.TypeCode.Should().Be(typeCode);
        metadata.DisplayName.Should().Be(displayName);
        metadata.Presentation.TableName.Should().Be(headTable);
        metadata.Presentation.DisplayColumn.Should().Be(displayField);
        metadata.Version.Should().Be(new CatalogMetadataVersion(1, "ab"));
        metadata.Tables.Should().ContainSingle(x => x.Kind == TableKind.Head);
    }

    [Theory]
    [MemberData(nameof(DocumentDefinitionCases))]
    public void Document_Definitions_Are_Registered_With_Expected_Presentation_And_Numbering(
        string typeCode,
        string displayName,
        string headTable,
        string? amountField,
        Type numberingPolicyType)
    {
        var definition = Registry.GetDocument(typeCode);
        var metadata = definition.Metadata;

        definition.TypeCode.Should().Be(typeCode);
        definition.NumberingPolicyType.Should().Be(numberingPolicyType);
        metadata.Presentation.DisplayName.Should().Be(displayName);
        metadata.Presentation.HasNumber.Should().BeTrue();
        metadata.Presentation.ComputedDisplay.Should().BeTrue();
        metadata.Presentation.HideSystemFieldsInEditor.Should().BeTrue();
        metadata.Presentation.AmountField.Should().Be(amountField);
        metadata.Version.Should().Be(new DocumentMetadataVersion(1, "ab"));
        metadata.Tables.Should().Contain(x => x.Kind == TableKind.Head && x.TableName == headTable);
    }

    [Theory]
    [MemberData(nameof(CatalogColumnCases))]
    public void Catalog_Columns_Expose_Required_Types_And_Lookups(
        string catalogType,
        string columnKey,
        ColumnType expectedType,
        bool expectedRequired,
        string? expectedLookupKind,
        string? expectedLookupTarget)
    {
        var column = FindCatalogColumn(catalogType, columnKey);

        column.ColumnType.Should().Be(expectedType);
        column.Required.Should().Be(expectedRequired);
        switch (expectedLookupKind)
        {
            case null:
                column.Lookup.Should().BeNull();
                break;
            case "catalog":
                column.Lookup.Should().BeOfType<CatalogLookupSourceMetadata>()
                    .Which.CatalogType.Should().Be(expectedLookupTarget);
                break;
            case "coa":
                column.Lookup.Should().BeOfType<ChartOfAccountsLookupSourceMetadata>();
                break;
        }
    }

    [Theory]
    [MemberData(nameof(DocumentHeadColumnCases))]
    public void Document_Head_Columns_Expose_Expected_Types_And_Lookups(
        string documentType,
        string columnKey,
        ColumnType expectedType,
        bool expectedRequired,
        string? expectedLookupKind,
        string? expectedLookupTarget)
    {
        var column = FindDocumentColumn(documentType, null, columnKey);

        column.Type.Should().Be(expectedType);
        column.Required.Should().Be(expectedRequired);
        switch (expectedLookupKind)
        {
            case null:
                column.Lookup.Should().BeNull();
                break;
            case "catalog":
                column.Lookup.Should().BeOfType<CatalogLookupSourceMetadata>()
                    .Which.CatalogType.Should().Be(expectedLookupTarget);
                break;
            case "document":
                column.Lookup.Should().BeOfType<DocumentLookupSourceMetadata>()
                    .Which.DocumentTypes.Should().ContainSingle().Which.Should().Be(expectedLookupTarget);
                break;
            case "coa":
                column.Lookup.Should().BeOfType<ChartOfAccountsLookupSourceMetadata>();
                break;
        }
    }

    [Theory]
    [MemberData(nameof(DocumentPartColumnCases))]
    public void Document_Part_Columns_Expose_Expected_Types_And_Lookups(
        string documentType,
        string partCode,
        string columnKey,
        ColumnType expectedType,
        bool expectedRequired,
        string? expectedLookupKind,
        string? expectedLookupTarget)
    {
        var column = FindDocumentColumn(documentType, partCode, columnKey);

        column.Type.Should().Be(expectedType);
        column.Required.Should().Be(expectedRequired);
        switch (expectedLookupKind)
        {
            case null:
                column.Lookup.Should().BeNull();
                break;
            case "catalog":
                column.Lookup.Should().BeOfType<CatalogLookupSourceMetadata>()
                    .Which.CatalogType.Should().Be(expectedLookupTarget);
                break;
            case "document":
                column.Lookup.Should().BeOfType<DocumentLookupSourceMetadata>()
                    .Which.DocumentTypes.Should().ContainSingle().Which.Should().Be(expectedLookupTarget);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(DocumentListFilterCases))]
    public void Document_List_Filters_Are_Configured_For_Key_Operational_Slices(
        string documentType,
        string filterKey,
        ColumnType expectedType,
        string expectedLookupKind,
        string expectedTarget)
    {
        var filter = Registry.GetDocument(documentType).Metadata.ListFilters!.Single(x => x.Key == filterKey);

        filter.Type.Should().Be(expectedType);
        filter.IsMulti.Should().BeTrue();
        switch (expectedLookupKind)
        {
            case "catalog":
                filter.Lookup.Should().BeOfType<CatalogLookupSourceMetadata>()
                    .Which.CatalogType.Should().Be(expectedTarget);
                break;
            case "document":
                filter.Lookup.Should().BeOfType<DocumentLookupSourceMetadata>()
                    .Which.DocumentTypes.Should().ContainSingle().Which.Should().Be(expectedTarget);
                break;
            case "coa":
                filter.Lookup.Should().BeOfType<ChartOfAccountsLookupSourceMetadata>();
                break;
        }
    }

    [Theory]
    [InlineData(AgencyBillingCodes.Client, "status", "Active", "On Hold", "Inactive")]
    [InlineData(AgencyBillingCodes.TeamMember, "member_type", "Employee", "Contractor", null)]
    [InlineData(AgencyBillingCodes.Project, "status", "Planned", "Active", "Completed")]
    [InlineData(AgencyBillingCodes.Project, "billing_model", "Time & Materials", null, null)]
    [InlineData(AgencyBillingCodes.ServiceItem, "unit_of_measure", "Hour", "Day", "Week")]
    public void Enum_Backend_Metadata_Uses_Display_Names(
        string catalogType,
        string fieldKey,
        string? firstExpected,
        string? secondExpected,
        string? thirdExpected)
    {
        var options = FindCatalogColumn(catalogType, fieldKey).Options ?? [];

        options.Select(x => x.Label).Should().Contain(firstExpected);
        if (secondExpected is not null)
            options.Select(x => x.Label).Should().Contain(secondExpected);
        if (thirdExpected is not null)
            options.Select(x => x.Label).Should().Contain(thirdExpected);
    }

    [Fact]
    public void SalesInvoice_Contract_Lookup_Declares_Mirrored_Based_On_Relationship()
    {
        var column = FindDocumentColumn(AgencyBillingCodes.SalesInvoice, null, "contract_id");

        column.MirroredRelationship.Should().NotBeNull();
        column.MirroredRelationship!.RelationshipCode.Should().Be("based_on");
    }

    [Fact]
    public void Runtime_Contributors_Extend_Definitions_With_Validators_Posting_And_Derivations()
    {
        var builder = new DefinitionsBuilder();
        new AgencyBillingDefinitionsContributor().Contribute(builder);
        new AgencyBillingCatalogValidationDefinitionsContributor().Contribute(builder);
        new AgencyBillingPostingDefinitionsContributor().Contribute(builder);
        new AgencyBillingDerivationDefinitionsContributor().Contribute(builder);
        var registry = builder.Build();

        registry.GetCatalog(AgencyBillingCodes.Project).ValidatorTypes.Should().Contain(typeof(ProjectCatalogUpsertValidator));
        registry.GetCatalog(AgencyBillingCodes.RateCard).ValidatorTypes.Should().Contain(typeof(RateCardCatalogUpsertValidator));
        registry.GetCatalog(AgencyBillingCodes.AccountingPolicy).ValidatorTypes.Should().Contain(typeof(AccountingPolicyCatalogUpsertValidator));

        registry.GetDocument(AgencyBillingCodes.ClientContract).PostValidatorTypes.Should().Contain(typeof(ClientContractPostValidator));
        registry.GetDocument(AgencyBillingCodes.ClientContract).ReferenceRegisterPostingHandlerType.Should().Be(typeof(ClientContractReferenceRegisterPostingHandler));
        registry.GetDocument(AgencyBillingCodes.Timesheet).PostValidatorTypes.Should().Contain(typeof(TimesheetPostValidator));
        registry.GetDocument(AgencyBillingCodes.Timesheet).OperationalRegisterPostingHandlerType.Should().Be(typeof(TimesheetOperationalRegisterPostingHandler));
        registry.GetDocument(AgencyBillingCodes.SalesInvoice).PostValidatorTypes.Should().Contain(typeof(SalesInvoicePostValidator));
        registry.GetDocument(AgencyBillingCodes.SalesInvoice).PostingHandlerType.Should().Be(typeof(SalesInvoicePostingHandler));
        registry.GetDocument(AgencyBillingCodes.SalesInvoice).OperationalRegisterPostingHandlerType.Should().Be(typeof(SalesInvoiceOperationalRegisterPostingHandler));
        registry.GetDocument(AgencyBillingCodes.CustomerPayment).PostValidatorTypes.Should().Contain(typeof(CustomerPaymentPostValidator));
        registry.GetDocument(AgencyBillingCodes.CustomerPayment).PostingHandlerType.Should().Be(typeof(CustomerPaymentPostingHandler));
        registry.GetDocument(AgencyBillingCodes.CustomerPayment).OperationalRegisterPostingHandlerType.Should().Be(typeof(CustomerPaymentOperationalRegisterPostingHandler));

        var derivation = registry.GetDocumentDerivation(AgencyBillingCodes.GenerateInvoiceDraftDerivation);
        derivation.Name.Should().Be("Generate Invoice Draft");
        derivation.FromTypeCode.Should().Be(AgencyBillingCodes.Timesheet);
        derivation.ToTypeCode.Should().Be(AgencyBillingCodes.SalesInvoice);
        derivation.RelationshipCodes.Should().ContainSingle().Which.Should().Be("created_from");
        derivation.HandlerType.Should().Be(typeof(GenerateInvoiceDraftFromTimesheetDerivationHandler));
    }

    [Fact]
    public void Canonical_Report_Definitions_Expose_Operational_And_Finance_Surfaces()
    {
        var source = new AgencyBillingCanonicalReportDefinitionSource();

        var definitions = source.GetDefinitions();

        definitions.Select(x => x.ReportCode).Should().Contain(
        [
            AgencyBillingCodes.UnbilledTimeReport,
            AgencyBillingCodes.ProjectProfitabilityReport,
            AgencyBillingCodes.InvoiceRegisterReport,
            AgencyBillingCodes.ArAgingReport,
            AgencyBillingCodes.TeamUtilizationReport,
        ]);
        definitions.Should().OnlyContain(x => x.Mode == NGB.Contracts.Reporting.ReportExecutionMode.Composable);
        definitions.Should().OnlyContain(x => x.Capabilities != null);
    }

    [Theory]
    [InlineData(AgencyBillingCodes.UnbilledTimeReport, "As of", "Operations")]
    [InlineData(AgencyBillingCodes.ProjectProfitabilityReport, "As of", "Finance")]
    [InlineData(AgencyBillingCodes.InvoiceRegisterReport, "From", "Billing")]
    [InlineData(AgencyBillingCodes.ArAgingReport, "As of", "Finance")]
    [InlineData(AgencyBillingCodes.TeamUtilizationReport, "From", "Delivery")]
    public void Canonical_Reports_Expose_Useful_Groups_And_Parameters(
        string reportCode,
        string expectedParameterLabel,
        string expectedGroup)
    {
        var definition = new AgencyBillingCanonicalReportDefinitionSource().GetDefinitions().Single(x => x.ReportCode == reportCode);

        definition.Group.Should().Be(expectedGroup);
        definition.Parameters.Should().NotBeNullOrEmpty();
        definition.Parameters!.Select(x => x.Label).Should().Contain(expectedParameterLabel);
        definition.Presentation.Should().NotBeNull();
        definition.Presentation!.InitialPageSize.Should().BeGreaterThan(0);
        definition.Filters.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(typeof(AgencyBillingClientContractNumberingPolicy), AgencyBillingCodes.ClientContract)]
    [InlineData(typeof(AgencyBillingTimesheetNumberingPolicy), AgencyBillingCodes.Timesheet)]
    [InlineData(typeof(AgencyBillingSalesInvoiceNumberingPolicy), AgencyBillingCodes.SalesInvoice)]
    [InlineData(typeof(AgencyBillingCustomerPaymentNumberingPolicy), AgencyBillingCodes.CustomerPayment)]
    public void Numbering_Policies_Create_Draft_Numbers_And_Not_Post_Numbers(Type policyType, string expectedTypeCode)
    {
        var policy = (NGB.Definitions.Documents.Numbering.IDocumentNumberingPolicy)Activator.CreateInstance(policyType)!;

        policy.TypeCode.Should().Be(expectedTypeCode);
        policy.EnsureNumberOnCreateDraft.Should().BeTrue();
        policy.EnsureNumberOnPost.Should().BeFalse();
    }

    public static IEnumerable<object[]> CatalogDefinitionCases()
    {
        yield return [AgencyBillingCodes.Client, "Client", "cat_ab_client", "display"];
        yield return [AgencyBillingCodes.TeamMember, "Team Member", "cat_ab_team_member", "display"];
        yield return [AgencyBillingCodes.Project, "Project", "cat_ab_project", "display"];
        yield return [AgencyBillingCodes.RateCard, "Rate Card", "cat_ab_rate_card", "display"];
        yield return [AgencyBillingCodes.ServiceItem, "Service Item", "cat_ab_service_item", "display"];
        yield return [AgencyBillingCodes.PaymentTerms, "Payment Terms", "cat_ab_payment_terms", "display"];
        yield return [AgencyBillingCodes.AccountingPolicy, "Accounting Policy", "cat_ab_accounting_policy", "display"];
    }

    public static IEnumerable<object[]> DocumentDefinitionCases()
    {
        yield return [AgencyBillingCodes.ClientContract, "Client Contract", "doc_ab_client_contract", null, typeof(AgencyBillingClientContractNumberingPolicy)];
        yield return [AgencyBillingCodes.Timesheet, "Timesheet", "doc_ab_timesheet", "amount", typeof(AgencyBillingTimesheetNumberingPolicy)];
        yield return [AgencyBillingCodes.SalesInvoice, "Sales Invoice", "doc_ab_sales_invoice", "amount", typeof(AgencyBillingSalesInvoiceNumberingPolicy)];
        yield return [AgencyBillingCodes.CustomerPayment, "Customer Payment", "doc_ab_customer_payment", "amount", typeof(AgencyBillingCustomerPaymentNumberingPolicy)];
    }

    public static IEnumerable<object[]> CatalogColumnCases()
    {
        yield return [AgencyBillingCodes.Client, "payment_terms_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.PaymentTerms];
        yield return [AgencyBillingCodes.Project, "client_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.Project, "project_manager_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.TeamMember];
        yield return [AgencyBillingCodes.RateCard, "service_item_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.ServiceItem];
        yield return [AgencyBillingCodes.ServiceItem, "default_revenue_account_id", ColumnType.Guid, false, "coa", null];
        yield return [AgencyBillingCodes.PaymentTerms, "due_days", ColumnType.Int32, true, null, null];
        yield return [AgencyBillingCodes.AccountingPolicy, "service_revenue_account_id", ColumnType.Guid, false, "coa", null];
    }

    public static IEnumerable<object[]> DocumentHeadColumnCases()
    {
        yield return [AgencyBillingCodes.ClientContract, "client_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.ClientContract, "project_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Project];
        yield return [AgencyBillingCodes.ClientContract, "payment_terms_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.PaymentTerms];
        yield return [AgencyBillingCodes.Timesheet, "team_member_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.TeamMember];
        yield return [AgencyBillingCodes.Timesheet, "project_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Project];
        yield return [AgencyBillingCodes.Timesheet, "client_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.SalesInvoice, "client_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.SalesInvoice, "project_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Project];
        yield return [AgencyBillingCodes.SalesInvoice, "contract_id", ColumnType.Guid, false, "document", AgencyBillingCodes.ClientContract];
        yield return [AgencyBillingCodes.CustomerPayment, "client_id", ColumnType.Guid, true, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.CustomerPayment, "cash_account_id", ColumnType.Guid, false, "coa", null];
    }

    public static IEnumerable<object[]> DocumentPartColumnCases()
    {
        yield return [AgencyBillingCodes.ClientContract, "lines", "service_item_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.ServiceItem];
        yield return [AgencyBillingCodes.ClientContract, "lines", "team_member_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.TeamMember];
        yield return [AgencyBillingCodes.ClientContract, "lines", "billing_rate", ColumnType.Decimal, true, null, null];
        yield return [AgencyBillingCodes.Timesheet, "lines", "service_item_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.ServiceItem];
        yield return [AgencyBillingCodes.Timesheet, "lines", "hours", ColumnType.Decimal, true, null, null];
        yield return [AgencyBillingCodes.Timesheet, "lines", "billable", ColumnType.Boolean, true, null, null];
        yield return [AgencyBillingCodes.Timesheet, "lines", "line_cost_amount", ColumnType.Decimal, false, null, null];
        yield return [AgencyBillingCodes.SalesInvoice, "lines", "service_item_id", ColumnType.Guid, false, "catalog", AgencyBillingCodes.ServiceItem];
        yield return [AgencyBillingCodes.SalesInvoice, "lines", "source_timesheet_id", ColumnType.Guid, false, "document", AgencyBillingCodes.Timesheet];
        yield return [AgencyBillingCodes.SalesInvoice, "lines", "quantity_hours", ColumnType.Decimal, true, null, null];
        yield return [AgencyBillingCodes.CustomerPayment, "applies", "sales_invoice_id", ColumnType.Guid, true, "document", AgencyBillingCodes.SalesInvoice];
        yield return [AgencyBillingCodes.CustomerPayment, "applies", "applied_amount", ColumnType.Decimal, true, null, null];
    }

    public static IEnumerable<object[]> DocumentListFilterCases()
    {
        yield return [AgencyBillingCodes.ClientContract, "client_id", ColumnType.Guid, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.ClientContract, "project_id", ColumnType.Guid, "catalog", AgencyBillingCodes.Project];
        yield return [AgencyBillingCodes.ClientContract, "payment_terms_id", ColumnType.Guid, "catalog", AgencyBillingCodes.PaymentTerms];
        yield return [AgencyBillingCodes.Timesheet, "team_member_id", ColumnType.Guid, "catalog", AgencyBillingCodes.TeamMember];
        yield return [AgencyBillingCodes.Timesheet, "project_id", ColumnType.Guid, "catalog", AgencyBillingCodes.Project];
        yield return [AgencyBillingCodes.Timesheet, "client_id", ColumnType.Guid, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.SalesInvoice, "client_id", ColumnType.Guid, "catalog", AgencyBillingCodes.Client];
        yield return [AgencyBillingCodes.SalesInvoice, "project_id", ColumnType.Guid, "catalog", AgencyBillingCodes.Project];
        yield return [AgencyBillingCodes.SalesInvoice, "contract_id", ColumnType.Guid, "document", AgencyBillingCodes.ClientContract];
        yield return [AgencyBillingCodes.CustomerPayment, "client_id", ColumnType.Guid, "catalog", AgencyBillingCodes.Client];
    }

    private static DefinitionsRegistry BuildRegistry()
    {
        var builder = new DefinitionsBuilder();
        new AgencyBillingDefinitionsContributor().Contribute(builder);
        return builder.Build();
    }

    private static CatalogColumnMetadata FindCatalogColumn(string typeCode, string columnKey)
    {
        var metadata = Registry.GetCatalog(typeCode).Metadata;
        return metadata.Tables
            .Single(x => x.Kind == TableKind.Head)
            .Columns
            .Single(x => x.ColumnName == columnKey);
    }

    private static DocumentColumnMetadata FindDocumentColumn(string typeCode, string? partCode, string columnKey)
    {
        var metadata = Registry.GetDocument(typeCode).Metadata;
        var table = partCode is null
            ? metadata.Tables.Single(x => x.Kind == TableKind.Head)
            : metadata.Tables.Single(x => x.Kind == TableKind.Part && x.PartCode == partCode);
        return table.Columns.Single(x => x.ColumnName == columnKey);
    }
}
