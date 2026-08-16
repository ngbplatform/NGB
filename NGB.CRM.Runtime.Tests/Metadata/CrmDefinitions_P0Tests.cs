using FluentAssertions;
using NGB.CRM.Definitions;
using NGB.CRM.Documents.Numbering;
using NGB.CRM.Runtime.Documents.Validation;
using NGB.CRM.Runtime.Posting;
using NGB.CRM.Runtime.Reporting;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;

namespace NGB.CRM.Runtime.Tests.Metadata;

public sealed class CrmDefinitions_P0Tests
{
    private static readonly DefinitionsRegistry Registry = BuildRegistry();

    [Theory]
    [InlineData(CrmCodes.Account, "Account", "cat_crm_account")]
    [InlineData(CrmCodes.Contact, "Contact", "cat_crm_contact")]
    [InlineData(CrmCodes.Product, "Product", "cat_crm_product")]
    [InlineData(CrmCodes.OpportunityStage, "Opportunity Stage", "cat_crm_opportunity_stage")]
    public void Catalog_Definitions_Are_Registered(string typeCode, string displayName, string headTable)
    {
        var definition = Registry.GetCatalog(typeCode);

        definition.TypeCode.Should().Be(typeCode);
        definition.Metadata.DisplayName.Should().Be(displayName);
        definition.Metadata.Presentation.TableName.Should().Be(headTable);
        definition.Metadata.Version.Should().Be(new CatalogMetadataVersion(1, "crm"));
        definition.Metadata.Tables.Should().ContainSingle(x => x.Kind == TableKind.Head);
    }

    [Theory]
    [InlineData(CrmCodes.LeadIntake, "Lead Intake", "doc_crm_lead_intake", typeof(CrmLeadIntakeNumberingPolicy))]
    [InlineData(CrmCodes.LeadQualification, "Lead Qualification", "doc_crm_lead_qualification", typeof(CrmLeadQualificationNumberingPolicy))]
    [InlineData(CrmCodes.LeadConversion, "Lead Conversion", "doc_crm_lead_conversion", typeof(CrmLeadConversionNumberingPolicy))]
    [InlineData(CrmCodes.OpportunityUpdate, "Opportunity Update", "doc_crm_opportunity_update", typeof(CrmOpportunityUpdateNumberingPolicy))]
    [InlineData(CrmCodes.Quote, "Quote", "doc_crm_quote", typeof(CrmQuoteNumberingPolicy))]
    [InlineData(CrmCodes.ActivityLog, "Activity Log", "doc_crm_activity_log", typeof(CrmActivityLogNumberingPolicy))]
    public void Document_Definitions_Are_Registered_With_Numbering(
        string typeCode,
        string displayName,
        string headTable,
        Type numberingPolicy)
    {
        var definition = Registry.GetDocument(typeCode);

        definition.TypeCode.Should().Be(typeCode);
        definition.NumberingPolicyType.Should().Be(numberingPolicy);
        var presentation = definition.Metadata.Presentation;
        presentation.Should().NotBeNull();
        presentation!.DisplayName.Should().Be(displayName);
        presentation.HasNumber.Should().BeTrue();
        presentation.ComputedDisplay.Should().BeTrue();
        definition.Metadata.Version.Should().Be(new DocumentMetadataVersion(1, "crm"));
        definition.Metadata.Tables.Should().Contain(x => x.Kind == TableKind.Head && x.TableName == headTable);
    }

    [Fact]
    public void Runtime_Contributor_Binds_Post_Validators_And_No_Gl_Posting_Hooks()
    {
        var document = Registry.GetDocument(CrmCodes.Quote);

        document.PostValidatorTypes.Should().Contain(typeof(QuotePostValidator));
        document.ReferenceRegisterPostingHandlerType.Should().Be(typeof(QuoteReferenceRegisterPostingHandler));
        document.PostingHandlerType.Should().BeNull();
        document.OperationalRegisterPostingHandlerType.Should().BeNull();
    }

    [Fact]
    public void Quote_Metadata_Declares_Line_Part_And_Product_Lookup()
    {
        var quote = Registry.GetDocument(CrmCodes.Quote).Metadata;
        var lines = quote.Tables.Single(x => x.Kind == TableKind.Part && x.PartCode == "lines");
        var product = lines.Columns.Single(x => x.ColumnName == "product_id");

        product.Type.Should().Be(ColumnType.Guid);
        product.Required.Should().BeTrue();
        product.Lookup.Should().BeOfType<CatalogLookupSourceMetadata>()
            .Which.CatalogType.Should().Be(CrmCodes.Product);
    }

    [Fact]
    public void LeadConversion_Metadata_Allows_Incomplete_Drafts_And_Binds_Post_Validation()
    {
        var conversion = Registry.GetDocument(CrmCodes.LeadConversion);
        var head = conversion.Metadata.Tables.Single(x => x.Kind == TableKind.Head);
        var account = head.Columns.Single(x => x.ColumnName == "account_id");
        var contact = head.Columns.Single(x => x.ColumnName == "contact_id");

        account.Required.Should().BeFalse(
            "a conversion derived from a qualified lead is saved before its account is selected");
        contact.Required.Should().BeFalse(
            "a conversion derived from a qualified lead is saved before its contact is selected");
        account.Lookup.Should().BeOfType<CatalogLookupSourceMetadata>()
            .Which.CatalogType.Should().Be(CrmCodes.Account);
        contact.Lookup.Should().BeOfType<CatalogLookupSourceMetadata>()
            .Which.CatalogType.Should().Be(CrmCodes.Contact);
        conversion.PostValidatorTypes.Should().Contain(typeof(LeadConversionPostValidator),
            "account and contact are mandatory at the Draft-to-Posted boundary");
    }

    [Fact]
    public void Canonical_Report_Definitions_Expose_Crm_Surfaces()
    {
        var reports = new CrmCanonicalReportDefinitionSource().GetDefinitions();

        reports.Select(x => x.ReportCode).Should().Contain(
        [
            CrmCodes.SalesPipelineReport,
            CrmCodes.OpportunityHistoryReport,
            CrmCodes.LeadConversionFunnelReport,
            CrmCodes.ActivitySummaryReport,
            CrmCodes.QuoteRegisterReport
        ]);
    }

    [Fact]
    public void Canonical_Report_Default_Layouts_Hide_Details_And_Expose_Drilldown_Groups()
    {
        var reports = new CrmCanonicalReportDefinitionSource().GetDefinitions();

        reports.Select(x => x.DefaultLayout?.ShowDetails).Should().OnlyContain(x => x == false);
        reports.Single(x => x.ReportCode == CrmCodes.SalesPipelineReport)
            .DefaultLayout!.RowGroups!.Select(x => x.FieldCode)
            .Should()
            .ContainInOrder("stage_display", "customer_display", "status", "opportunity_display");
        reports.Single(x => x.ReportCode == CrmCodes.ActivitySummaryReport)
            .DefaultLayout!.RowGroups!.Select(x => x.FieldCode)
            .Should()
            .Contain(["activity_type", "customer_display", "contact_display", "outcome"]);
        reports.Single(x => x.ReportCode == CrmCodes.QuoteRegisterReport)
            .DefaultLayout!.RowGroups!.Select(x => x.FieldCode)
            .Should()
            .Contain(["quote_status", "customer_display", "contact_display", "currency"]);
        reports.Single(x => x.ReportCode == CrmCodes.OpportunityHistoryReport)
            .DefaultLayout!.RowGroups!.Select(x => x.FieldCode)
            .Should()
            .ContainInOrder("customer_display", "opportunity_display", "stage_display");
        reports.Single(x => x.ReportCode == CrmCodes.LeadConversionFunnelReport)
            .DefaultLayout!.RowGroups!.Select(x => x.FieldCode)
            .Should()
            .ContainInOrder("funnel_step", "document_display");

        var quoteFields = reports.Single(x => x.ReportCode == CrmCodes.QuoteRegisterReport).Dataset!.Fields!;
        quoteFields.Single(x => x.Code == "customer_id")
            .Should()
            .Match<NGB.Contracts.Reporting.ReportFieldDto>(x =>
                x.IsFilterable && !x.IsGroupable && !x.IsSortable && !x.IsSelectable);
        quoteFields.Single(x => x.Code == "customer_display")
            .Should()
            .Match<NGB.Contracts.Reporting.ReportFieldDto>(x =>
                x.IsGroupable && x.IsSortable && x.IsSelectable);
        reports.Single(x => x.ReportCode == CrmCodes.SalesPipelineReport).Dataset!.Fields!
            .Single(x => x.Code == "opportunity_id")
            .Should()
            .Match<NGB.Contracts.Reporting.ReportFieldDto>(x =>
                x.IsFilterable && !x.IsGroupable && !x.IsSortable && !x.IsSelectable);

        reports.Single(x => x.ReportCode == CrmCodes.SalesPipelineReport).Dataset!.Fields!
            .Single(x => x.Code == "expected_close_date")
            .IsFilterable.Should().BeTrue();
        reports.Single(x => x.ReportCode == CrmCodes.OpportunityHistoryReport).Dataset!.Fields!
            .Single(x => x.Code == "event_at_utc")
            .IsFilterable.Should().BeTrue();
        reports.Single(x => x.ReportCode == CrmCodes.LeadConversionFunnelReport).Dataset!.Fields!
            .Single(x => x.Code == "event_at_utc")
            .IsFilterable.Should().BeTrue();
        reports.Single(x => x.ReportCode == CrmCodes.ActivitySummaryReport).Dataset!.Fields!
            .Single(x => x.Code == "activity_date")
            .IsFilterable.Should().BeTrue();
        reports.Single(x => x.ReportCode == CrmCodes.QuoteRegisterReport).Dataset!.Fields!
            .Single(x => x.Code == "quote_date")
            .IsFilterable.Should().BeTrue();
    }

    private static DefinitionsRegistry BuildRegistry()
    {
        var builder = new DefinitionsBuilder();
        new CrmDefinitionsContributor().Contribute(builder);
        new CrmPostingDefinitionsContributor().Contribute(builder);
        return builder.Build();
    }
}
