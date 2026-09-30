using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Runtime;
using NGB.Testing.Reporting;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Reports;

[CollectionDefinition("CRM report scale", DisableParallelization = true)]
public sealed class CrmReportScaleCollection : ICollectionFixture<CrmReportScaleFixture>;

public sealed class CrmReportScaleFixture : IAsyncLifetime
{
    private readonly CrmPostgresFixture _database = new();
    public IHost Host { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        Host = CrmHostFactory.Create(_database.ConnectionString, services =>
        {
            services.RemoveAll<CrmDemoSeedOptions>();
            services.AddSingleton(new CrmDemoSeedOptions { GeneratedAccountCount = 500, GeneratedOpportunityCycleCount = 2000 });
        });
        await using var scope = Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICrmDemoSeedService>().EnsureDemoAsync(default);
        result.DocumentsCreated.Should().BeGreaterThan(10000);
    }
    public async Task DisposeAsync() { Host?.Dispose(); await _database.DisposeAsync(); }
}

[Collection("CRM report scale")]
public sealed class CrmReporting_ScaleTests(CrmReportScaleFixture fixture)
{
    [Theory]
    [InlineData(CrmCodes.SalesPipelineReport)]
    [InlineData(CrmCodes.OpportunityHistoryReport)]
    [InlineData(CrmCodes.LeadConversionFunnelReport)]
    [InlineData(CrmCodes.ActivitySummaryReport)]
    [InlineData(CrmCodes.QuoteRegisterReport)]
    public async Task Packaged_reports_bound_page_queries_and_complete_exports_on_a_large_business_dataset(string code)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await ReportScaleAssertions.VerifyAsync(scope.ServiceProvider.GetRequiredService<IReportEngine>(),
            scope.ServiceProvider.GetRequiredService<IReportDownloadService>(), code, new ReportExecutionRequestDto(), minimumExportRows: 2000);
    }
}
