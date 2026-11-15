extern alias Monitor;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NgbApplication.IntegrationTests;

public sealed class WatchdogHostTests
{
    [Fact]
    public async Task Actual_monitor_checks_its_web_target_and_protects_the_dashboard_and_data()
    {
        await using var identity = new TestIdentity();
        await identity.StartAsync();
        await using var application = new WebApplicationFactory<Monitor::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("KeycloakSettings:Issuer", identity.Issuer);
            builder.UseSetting("KeycloakSettings:RequireHttpsMetadata", "false");
            builder.UseSetting("KeycloakSettings:ClientIds:0", TestIdentity.ClientId);
            builder.UseSetting("WebClient", identity.Issuer);
        });
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://127.0.0.1"),
            AllowAutoRedirect = false
        });

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var report = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", report.RootElement.GetProperty("status").GetString());
        Assert.True(report.RootElement.GetProperty("entries").TryGetProperty("Web Client (Vue.js)", out _));
        foreach (var path in new[] { "/health-ui", "/health-ui-api" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith(identity.Issuer, response.Headers.Location!.AbsoluteUri);
        }
    }
}
