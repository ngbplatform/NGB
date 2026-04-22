using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NGB.Watchdog.HealthChecks;
using NGB.Watchdog.Hosting;
using Xunit;

namespace NGB.Watchdog.IntegrationTests.Hosting;

public sealed class WatchdogHosting_HttpSurface_And_WebClientHealth_P0Tests
{
    [Fact]
    public async Task Health_Endpoint_Returns_Healthy_When_WebClient_Is_Reachable()
    {
        await using var target = await StartProbeServerAsync(_ => Results.Ok(new { status = "ok" }));
        await using var watchdog = await StartWatchdogAsync(
            target,
            options =>
            {
                options.RequireAuthorization = false;
                options.MapAccountEndpoints = false;
                options.PageTitle = "Test Health";
            },
            addWebClient: true);

        using var client = watchdog.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = await ReadJsonAsync(response);
        payload.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        payload.RootElement
            .GetProperty("entries")
            .GetProperty("Web Client (Vue.js)")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Healthy");
    }

    [Fact]
    public async Task Health_Endpoint_Returns_Unhealthy_When_WebClient_Returns_Failure_Status()
    {
        await using var target = await StartProbeServerAsync(_ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        await using var watchdog = await StartWatchdogAsync(
            target,
            options =>
            {
                options.RequireAuthorization = false;
                options.MapAccountEndpoints = false;
            },
            addWebClient: true);

        using var client = watchdog.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using var payload = await ReadJsonAsync(response);
        payload.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");

        var entry = payload.RootElement
            .GetProperty("entries")
            .GetProperty("Web Client (Vue.js)");

        entry.GetProperty("status").GetString().Should().Be("Unhealthy");
        entry.GetProperty("description").GetString().Should().Match(s =>
            !string.IsNullOrWhiteSpace(s) &&
            (s.Contains("ServiceUnavailable", StringComparison.Ordinal) ||
             s.Contains("503", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Ui_And_Account_Endpoints_Are_Mapped_When_Enabled()
    {
        await using var target = await StartProbeServerAsync(_ => Results.Ok(new { status = "ok" }));
        await using var watchdog = await StartWatchdogAsync(target, options =>
        {
            options.RequireAuthorization = false;
            options.MapAccountEndpoints = true;
            options.PageTitle = "NGB: Test Watchdog";
        });

        using var client = watchdog.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        using (var uiResponse = await client.GetAsync("/health-ui"))
        {
            uiResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            uiResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

            var html = await uiResponse.Content.ReadAsStringAsync();
            html.Should().Contain("NGB: Test Watchdog");
        }

        using (var logoutResponse = await client.PostAsync("/account/local-logout", content: null))
        {
            logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var logoutPageResponse = await client.GetAsync("/account/logout"))
        {
            logoutPageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            logoutPageResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

            var html = await logoutPageResponse.Content.ReadAsStringAsync();
            html.Should().Contain("action='/account/logout'");
            html.Should().Contain("Logout");
        }

        using (var accessDeniedResponse = await client.GetAsync("/Account/AccessDenied"))
        {
            accessDeniedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            accessDeniedResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

            var html = await accessDeniedResponse.Content.ReadAsStringAsync();
            html.Should().Contain("Access Denied. You have no permissions.");
        }
    }

    [Fact]
    public async Task Account_Endpoints_Are_Not_Mapped_When_Disabled()
    {
        await using var target = await StartProbeServerAsync(_ => Results.Ok(new { status = "ok" }));
        await using var watchdog = await StartWatchdogAsync(target, options =>
        {
            options.RequireAuthorization = false;
            options.MapAccountEndpoints = false;
        });

        using var client = watchdog.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        using (var logoutPageResponse = await client.GetAsync("/account/logout"))
        {
            logoutPageResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var logoutResponse = await client.PostAsync("/account/local-logout", content: null))
        {
            logoutResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var accessDeniedResponse = await client.GetAsync("/Account/AccessDenied"))
        {
            accessDeniedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    private static async Task<WebApplication> StartProbeServerAsync(Func<HttpContext, IResult> handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapGet("/{**path}", handler);
        await app.StartAsync();

        return app;
    }

    private static async Task<WebApplication> StartWatchdogAsync(
        WebApplication target,
        Action<WatchdogOptions> configure,
        bool addWebClient = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebClient"] = "https://web-client.test/ping",
            ["KeycloakSettings:Issuer"] = "https://example.invalid/realms/ngb",
            ["KeycloakSettings:RequireHttpsMetadata"] = bool.FalseString,
            ["KeycloakSettings:ClientIds:0"] = "ngb-watchdog-admin",
        });

        var healthChecks = builder.AddNgbWatchdog(configure);
        if (addWebClient)
            healthChecks.AddWebClient();

        builder.Services
            .AddHttpClient("HealthCheckHttpClient")
            .ConfigurePrimaryHttpMessageHandler(() => target.GetTestServer().CreateHandler());

        var app = builder.Build();
        app.UseNgbWatchdog();
        app.MapNgbWatchdog();
        await app.StartAsync();

        return app;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }
}
