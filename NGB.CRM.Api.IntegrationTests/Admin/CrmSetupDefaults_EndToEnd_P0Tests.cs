using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.CRM.Runtime;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Admin;

[Collection(CrmPostgresCollection.Name)]
public sealed class CrmSetupDefaults_EndToEnd_P0Tests(CrmPostgresFixture fixture)
{
    [Fact]
    public async Task EnsureDefaults_Is_Idempotent_And_Creates_Stages_And_Products()
    {
        await fixture.ResetDatabaseAsync();

        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await host.StartAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var setup = scope.ServiceProvider.GetRequiredService<ICrmSetupService>();
            var first = await setup.EnsureDefaultsAsync();
            var second = await setup.EnsureDefaultsAsync();

            first.OpportunityStagesEnsured.Should().BeGreaterThan(0);
            first.ProductsEnsured.Should().BeGreaterThan(0);
            second.OpportunityStagesEnsured.Should().Be(0);
            second.ProductsEnsured.Should().Be(0);
        }

        (await CountAsync(fixture.ConnectionString, "cat_crm_opportunity_stage")).Should().Be(6);
        (await CountAsync(fixture.ConnectionString, "cat_crm_product")).Should().Be(2);

        await host.StopAsync();
    }

    private static async Task<int> CountAsync(string cs, string tableName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*)::int FROM public.{tableName};", conn);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
