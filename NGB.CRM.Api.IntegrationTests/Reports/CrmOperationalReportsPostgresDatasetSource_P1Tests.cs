using FluentAssertions;
using NGB.CRM.PostgreSql.Reporting;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Reports;

public sealed class CrmOperationalReportsPostgresDatasetSource_P1Tests
{
    [Fact]
    public void Operational_reports_use_typed_read_models_instead_of_scanning_reference_register_history()
    {
        var datasets = new CrmOperationalReportsPostgresDatasetSource().GetDatasets();

        datasets.Should().HaveCount(5);
        datasets.Should().OnlyContain(static dataset =>
            !dataset.FromSql.Contains("refreg_crm_", StringComparison.OrdinalIgnoreCase));
        datasets.Single(static dataset => dataset.DatasetCodeNorm == CrmCodes.SalesPipelineReport)
            .FromSql.Should().Contain("crm_opportunities_current");
        datasets.Single(static dataset => dataset.DatasetCodeNorm == CrmCodes.OpportunityHistoryReport)
            .FromSql.Should().Contain("crm_opportunity_history");
        datasets.Single(static dataset => dataset.DatasetCodeNorm == CrmCodes.ActivitySummaryReport)
            .FromSql.Should().Contain("crm_activities_current");
        datasets.Single(static dataset => dataset.DatasetCodeNorm == CrmCodes.QuoteRegisterReport)
            .FromSql.Should().Contain("crm_quotes_current");
        datasets.Single(static dataset => dataset.DatasetCodeNorm == CrmCodes.LeadConversionFunnelReport)
            .FromSql.Should().Contain("doc_crm_lead_qualification");
    }
}
