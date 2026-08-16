using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Reports;

[Collection(CrmDocumentsPostgresCollection.Name)]
public sealed class CrmReportDefinitions_EndToEnd_P0Tests(CrmPostgresFixture fixture)
{
    [Fact]
    public async Task Crm_Host_Exposes_Canonical_Report_Definitions()
    {
        await fixture.ResetDatabaseAsync();

        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await host.StartAsync();

        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>();
        var definitions = await provider.GetAllDefinitionsAsync(CancellationToken.None);

        definitions.Select(static x => x.ReportCode).Should().Contain([
            CrmCodes.SalesPipelineReport,
            CrmCodes.OpportunityHistoryReport,
            CrmCodes.LeadConversionFunnelReport,
            CrmCodes.ActivitySummaryReport,
            CrmCodes.QuoteRegisterReport
        ]);

        await host.StopAsync();
    }
}
