using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NGB.Tools.Exceptions;
using NGB.Watchdog.Hosting;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class WatchdogHttpTests
{
    [Fact]
    public async Task Packaged_host_reports_live_web_client_failure_and_recovery()
    {
        var status = StatusCodes.Status200OK;
        await using var probe = CreateBuilder().Build();
        probe.MapGet("/", () => Results.StatusCode(status));
        await probe.StartAsync();

        var builder = CreateBuilder(Origin(probe).ToString());
        builder.AddNgbWatchdog("Equipment operations");
        await using var watchdog = builder.Build();
        watchdog.UseNgbWatchdog().MapNgbWatchdog();
        await watchdog.StartAsync();
        using var client = new HttpClient { BaseAddress = Origin(watchdog) };

        foreach (var expected in new[] { true, false, true })
        {
            status = expected ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
            using var response = await client.GetAsync("/health");
            Assert.Equal(expected ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expected ? "Healthy" : "Unhealthy", body.GetProperty("status").GetString());
            var entry = body.GetProperty("entries").GetProperty("Web Client (Vue.js)");
            Assert.Equal(expected ? "Healthy" : "Unhealthy", entry.GetProperty("status").GetString());
            Assert.DoesNotContain("connectionstring", body.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        var options = watchdog.Services.GetRequiredService<IOptions<WatchdogOptions>>().Value;
        Assert.True(options.RequireAuthorization);
        Assert.True(options.MapAccountEndpoints);
        Assert.Equal("dashboard.css", Assert.Single(options.CustomStylesheets));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "dashboard.css")));
    }

    [Fact]
    public async Task Custom_routes_keep_admin_authorization_and_branding_with_real_test_cookies()
    {
        var builder = CreateBuilder();
        builder.AddNgbWatchdog(options =>
        {
            options.HealthPath = " readiness/ ";
            options.UiPath = " operations/ ";
            options.ApiPath = " operations-data/ ";
            options.PageTitle = " Equipment service status ";
            options.MapAccountEndpoints = false;
        });
        builder.Services.AddAuthentication(options =>
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme);
        builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                // Only the test transport uses HTTP and synthetic cookie sign-in.
                // The platform's AuthAdminPolicy remains unchanged.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        await using var watchdog = builder.Build();
        watchdog.UseNgbWatchdog().MapNgbWatchdog();
        watchdog.MapPost("/__test/sign-in/{role}", async (HttpContext context, string role) =>
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, role)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        });
        await watchdog.StartAsync();
        var options = watchdog.Services.GetRequiredService<IOptions<WatchdogOptions>>().Value;
        Assert.Equal("/readiness", options.HealthPath);
        Assert.Equal("/operations", options.UiPath);
        Assert.Equal("/operations-data", options.ApiPath);
        Assert.Equal("Equipment service status", options.PageTitle);

        using var anonymous = Client(watchdog);
        using var health = await anonymous.GetAsync("/readiness");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var anonymousUi = await anonymous.GetAsync("/operations");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousUi.StatusCode);
        foreach (var role in new[] { "reader", "ngb-admin" })
        {
            using var client = Client(watchdog);
            using var signIn = await client.PostAsync("/__test/sign-in/" + role, null);
            signIn.EnsureSuccessStatusCode();
            using var ui = await client.GetAsync("/operations");
            using var api = await client.GetAsync("/operations-data");
            if (role == "reader")
            {
                Assert.Equal(HttpStatusCode.Forbidden, ui.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
                Assert.Equal(HttpStatusCode.OK, api.StatusCode);
                var html = await ui.Content.ReadAsStringAsync();
                Assert.Contains("Equipment service status", html);
                Assert.Contains("ngb-watchdog-dashboard-favicon", html);
                Assert.Contains("</head>", html);
            }
        }
    }

    [Fact]
    public async Task Configuration_rejects_missing_paths_titles_and_stylesheets_before_listening()
    {
        foreach (var configure in new Action<WatchdogOptions>[]
        {
            options => options.HealthPath = " ",
            options => options.UiPath = " ",
            options => options.ApiPath = " ",
            options => options.PageTitle = " ",
        })
        {
            Assert.Throws<NgbArgumentRequiredException>(() => CreateBuilder().AddNgbWatchdog(configure));
        }
        Assert.Throws<NgbArgumentRequiredException>(() => new WatchdogOptions().AddCustomStylesheet(" "));
        var builder = CreateBuilder();
        builder.AddNgbWatchdog(options => options.AddCustomStylesheet("missing-" + Guid.NewGuid() + ".css"));
        await using var watchdog = builder.Build();
        Assert.Throws<NgbConfigurationViolationException>(() => watchdog.MapNgbWatchdog());
    }

    private static WebApplicationBuilder CreateBuilder(string? webClient = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ApplicationName = typeof(WatchdogHttpTests).Assembly.GetName().Name,
        });
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeycloakSettings:Issuer"] = "https://identity.example.invalid/realms/test",
            ["KeycloakSettings:ClientIds:0"] = "equipment-watchdog",
            ["WebClient"] = webClient,
        });
        return builder;
    }

    private static Uri Origin(WebApplication application) => new(application.Services
        .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());

    private static HttpClient Client(WebApplication application) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        CookieContainer = new CookieContainer(),
    }) { BaseAddress = Origin(application) };
}
