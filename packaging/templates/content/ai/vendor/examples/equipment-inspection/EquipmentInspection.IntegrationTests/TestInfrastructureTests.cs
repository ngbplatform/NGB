using System.Net;
using NGB.Contracts.Services;
using Npgsql;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class TestInfrastructureTests
{
    [Fact]
    public async Task Parallel_hosts_share_servers_but_isolate_databases_realms_and_tokens()
    {
        await using var first = new InspectionHost();
        await using var second = new InspectionHost();
        await Task.WhenAll(first.StartAsync(), second.StartAsync());
        var firstDatabase = new NpgsqlConnectionStringBuilder(first.ConnectionString);
        var secondDatabase = new NpgsqlConnectionStringBuilder(second.ConnectionString);
        Assert.Equal(firstDatabase.Host, secondDatabase.Host);
        Assert.Equal(firstDatabase.Port, secondDatabase.Port);
        Assert.NotEqual(firstDatabase.Database, secondDatabase.Database);
        Assert.Equal(new Uri(first.IdentityIssuer).Authority, new Uri(second.IdentityIssuer).Authority);
        Assert.NotEqual(first.IdentityIssuer, second.IdentityIssuer);

        using var firstAdmin = await first.CreateClientAsync("administrator");
        using var secondAdmin = await second.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(firstAdmin, "/api/catalogs/example.equipment",
            Payload(("display", "Isolated equipment")));
        using var absent = await secondAdmin.GetAsync($"/api/catalogs/example.equipment/{equipment.Id}");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        using var foreignToken = second.CreateAnonymousClient();
        foreignToken.DefaultRequestHeaders.Authorization = firstAdmin.DefaultRequestHeaders.Authorization;
        using var denied = await foreignToken.GetAsync("/api/example-access/registered-permission");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var allowed = await secondAdmin.GetAsync("/api/example-access/registered-permission");
        await EnsureSuccessAsync(allowed);
    }

    [Fact]
    public async Task Disposing_one_host_removes_its_resources_and_preserves_another_host()
    {
        var first = new InspectionHost();
        string connectionString;
        string issuer;
        await using var second = new InspectionHost();
        await using (first)
        {
            await Task.WhenAll(first.StartAsync(), second.StartAsync());
            connectionString = first.ConnectionString;
            issuer = first.IdentityIssuer;
        }

        using var client = new HttpClient();
        using var deleted = await client.GetAsync(issuer + "/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        await using var connection = new NpgsqlConnection(connectionString);
        var error = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InvalidCatalogName, error.SqlState);

        using var admin = await second.CreateClientAsync("administrator");
        using var permission = await admin.GetAsync("/api/example-access/registered-permission");
        await EnsureSuccessAsync(permission);
    }
}
