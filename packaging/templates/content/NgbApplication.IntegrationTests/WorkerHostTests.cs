extern alias Worker;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.PostgreSql.Bootstrap;
using Testcontainers.PostgreSql;
using Xunit;

namespace NgbApplication.IntegrationTests;

public sealed class WorkerHostTests
{
    [Fact]
    public async Task Actual_worker_host_reports_storage_health_and_protects_its_dashboard()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await DatabaseBootstrapper.InitializeAsync(postgres.GetConnectionString(), default);
        await using var identity = new TestIdentity();
        await identity.StartAsync();

        await using var application = new WebApplicationFactory<Worker::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", postgres.GetConnectionString());
            builder.UseSetting("KeycloakSettings:Issuer", identity.Issuer);
            builder.UseSetting("KeycloakSettings:RequireHttpsMetadata", "false");
            builder.UseSetting("KeycloakSettings:ClientIds:0", TestIdentity.ClientId);
        });
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://127.0.0.1"),
            AllowAutoRedirect = false
        });
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using var dashboard = await client.GetAsync("/hangfire");
        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.StartsWith(identity.Issuer, dashboard.Headers.Location!.AbsoluteUri);
    }
}
