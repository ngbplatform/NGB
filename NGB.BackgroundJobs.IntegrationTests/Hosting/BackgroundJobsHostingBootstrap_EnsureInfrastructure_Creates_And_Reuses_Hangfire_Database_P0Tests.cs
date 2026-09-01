using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.PostgreSql;
using NGB.PostgreSql.Bootstrap;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hosting;

[Collection(HangfirePostgresCollection.Name)]
public sealed class BackgroundJobsHostingBootstrap_EnsureInfrastructure_Creates_And_Reuses_Hangfire_Database_P0Tests
{
    private readonly HangfirePostgresFixture _fixture;

    public BackgroundJobsHostingBootstrap_EnsureInfrastructure_Creates_And_Reuses_Hangfire_Database_P0Tests(HangfirePostgresFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task EnsureInfrastructureAsync_Creates_HangfireDatabase_And_Is_Idempotent()
    {
        var hangfireDbName = $"ngb_hangfire_{Guid.NewGuid():N}";
        var hangfireConnectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = hangfireDbName
        }.ConnectionString;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _fixture.ConnectionString,
            ["ConnectionStrings:Hangfire"] = hangfireConnectionString,
            ["KeycloakSettings:Issuer"] = "https://example.invalid/realms/ngb",
            ["KeycloakSettings:RequireHttpsMetadata"] = bool.FalseString,
            ["KeycloakSettings:ClientIds:0"] = "ngb-admin-console",
            ["BackgroundJobs:Enabled"] = bool.FalseString,
        });

        var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create, options =>
        {
            options.HangfireConnectionStringName = "Hangfire";
            options.RequireDashboardAuthorization = false;
            options.MapAccountEndpoints = false;
            options.DashboardStylesheetPaths.Clear();
        });

        var provisioner = new PostgresDatabaseProvisioner();
        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => bootstrap.EnsureInfrastructureAsync(provisioner)));
        await bootstrap.EnsureInfrastructureAsync(provisioner);

        await using var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = "postgres"
        }.ConnectionString);
        await conn.OpenAsync();

        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name);",
            new { name = hangfireDbName });

        exists.Should().BeTrue();
    }
}
