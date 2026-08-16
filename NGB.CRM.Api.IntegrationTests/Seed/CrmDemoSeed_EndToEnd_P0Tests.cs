using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Api.IntegrationTests.Support;
using NGB.CRM.Runtime;
using NGB.Contracts.Reporting;
using Npgsql;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Seed;

[Collection(CrmSeedPostgresCollection.Name)]
public sealed class CrmDemoSeed_EndToEnd_P0Tests(CrmPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [VolumeFact]
    [Trait("Category", "Volume")]
    public async Task EnsureDemo_Is_Idempotent_And_Populates_Crm_Operational_Reports()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var demoSeed = scope.ServiceProvider.GetRequiredService<ICrmDemoSeedService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        var first = await demoSeed.EnsureDemoAsync(CancellationToken.None);
        first.AccountsEnsured.Should().Be(83);
        first.ContactsEnsured.Should().Be(83);
        first.ProductsEnsured.Should().Be(2);
        first.StagesEnsured.Should().Be(6);
        first.DocumentsCreated.Should().Be(3134);
        first.SeededOperationalData.Should().BeTrue();

        var second = await demoSeed.EnsureDemoAsync(CancellationToken.None);
        second.DocumentsCreated.Should().Be(0);
        second.SeededOperationalData.Should().BeFalse();

        (await CrmIntegrationTestHelpers.CountDocumentsAsync(documents, CrmCodes.LeadIntake)).Should().Be(523);
        (await CrmIntegrationTestHelpers.CountDocumentsAsync(documents, CrmCodes.LeadQualification)).Should().Be(523);
        (await CrmIntegrationTestHelpers.CountDocumentsAsync(documents, CrmCodes.LeadConversion)).Should().Be(522);
        (await CrmIntegrationTestHelpers.CountDocumentsAsync(documents, CrmCodes.OpportunityUpdate)).Should().Be(522);
        (await CrmIntegrationTestHelpers.CountDocumentsAsync(documents, CrmCodes.Quote)).Should().Be(521);
        (await CrmIntegrationTestHelpers.CountDocumentsAsync(documents, CrmCodes.ActivityLog)).Should().Be(523);

        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_lead_funnel__records WHERE NOT is_deleted;")).Should().Be(1568);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_opportunities__records WHERE NOT is_deleted;")).Should().Be(1044);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_quotes__records WHERE NOT is_deleted;")).Should().Be(521);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_activities__records WHERE NOT is_deleted;")).Should().Be(523);

        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM crm_opportunities_current WHERE status IN ('Open', 'Won');")).Should().Be(470);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM crm_quotes_current WHERE quote_status = 'Presented';")).Should().Be(365);
        (await CrmIntegrationTestHelpers.ScalarDecimalAsync(
            fixture.ConnectionString,
            "SELECT COALESCE(SUM(amount), 0) FROM crm_quotes_current WHERE quote_status = 'Presented';")).Should().Be(16808182.5m);

        var pipeline = await reports.ExecuteAsync(
            CrmCodes.SalesPipelineReport,
            new ReportExecutionRequestDto(DisablePaging: true),
            CancellationToken.None);
        CrmIntegrationTestHelpers.SumMeasure(pipeline, "amount").Should().Be(41927750m);
        CrmIntegrationTestHelpers.SumMeasure(pipeline, "weighted_amount").Should().Be(24271790m);

        var funnel = await reports.ExecuteAsync(
            CrmCodes.LeadConversionFunnelReport,
            new ReportExecutionRequestDto(DisablePaging: true),
            CancellationToken.None);
        CrmIntegrationTestHelpers.SumMeasure(funnel, "lead_count").Should().Be(1568m);

        var activities = await reports.ExecuteAsync(
            CrmCodes.ActivitySummaryReport,
            new ReportExecutionRequestDto(DisablePaging: true),
            CancellationToken.None);
        CrmIntegrationTestHelpers.SumMeasure(activities, "activity_count").Should().Be(523m);

        await ClearCrmReferenceRegisterRowsAndStateAsync(fixture.ConnectionString);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_lead_funnel__records WHERE NOT is_deleted;")).Should().Be(0);

        var repaired = await demoSeed.EnsureDemoAsync(CancellationToken.None);
        repaired.DocumentsCreated.Should().Be(0);
        repaired.SeededOperationalData.Should().BeTrue();

        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_lead_funnel__records WHERE NOT is_deleted;")).Should().Be(1568);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_opportunities__records WHERE NOT is_deleted;")).Should().Be(1044);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_quotes__records WHERE NOT is_deleted;")).Should().Be(521);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM refreg_crm_activities__records WHERE NOT is_deleted;")).Should().Be(523);
    }

    private static async Task ClearCrmReferenceRegisterRowsAndStateAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var cmd = new NpgsqlCommand(
                         """
                         TRUNCATE TABLE
                             refreg_crm_lead_funnel__records,
                             refreg_crm_opportunities__records,
                             refreg_crm_quotes__records,
                             refreg_crm_activities__records;
                         DELETE FROM reference_register_write_state
                         WHERE register_id IN (
                             SELECT register_id
                             FROM reference_registers
                             WHERE code_norm IN (
                                 'crm.lead_funnel',
                                 'crm.opportunities',
                                 'crm.quotes',
                                 'crm.activities'
                             )
                         );
                         """,
                         conn,
                         tx))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}
