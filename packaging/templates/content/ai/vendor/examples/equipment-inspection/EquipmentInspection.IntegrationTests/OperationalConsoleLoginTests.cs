using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using EquipmentInspection.BackgroundJobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NGB.Watchdog.Hosting;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class OperationalConsoleLoginTests
{
    [Theory]
    [InlineData("administrator", HttpStatusCode.OK)]
    [InlineData("reader", HttpStatusCode.Redirect)]
    public async Task Local_watchdog_completes_real_oidc_callback_and_keeps_admin_authorization(string username, HttpStatusCode expected)
    {
        var port = FreePort();
        var origin = $"http://127.0.0.1:{port}";
        await using var identity = new TestIdentity(origin);
        await identity.StartAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(OperationalConsoleLoginTests).Assembly.GetName().Name
        });
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeycloakSettings:Issuer"] = identity.Issuer,
            ["KeycloakSettings:RequireHttpsMetadata"] = "false",
            ["KeycloakSettings:ClientIds:0"] = TestIdentity.ClientId
        });
        builder.AddNgbWatchdog(options => options.PageTitle = "Equipment service health");
        builder.Services.ConfigureOperationalConsole(origin);
        await using var application = builder.Build();
        application.UseNgbWatchdog().MapNgbWatchdog();
        await application.StartAsync();

        await AssertLoginAsync(identity, origin, username, expected,
            "/health-ui", "Equipment service health", "/health-ui-api");
    }

    [Theory]
    [InlineData("administrator", HttpStatusCode.OK)]
    [InlineData("reader", HttpStatusCode.Redirect)]
    public async Task Local_worker_completes_real_oidc_callback_and_keeps_admin_authorization(string username, HttpStatusCode expected)
    {
        var port = FreePort();
        var origin = $"http://127.0.0.1:{port}";
        await using var identity = new TestIdentity(origin);
        await identity.StartAsync();
        await using var host = new InspectionHost();
        await host.StartAsync();
        await using var worker = new WebApplicationFactory<BackgroundJobs.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", host.ConnectionString);
            builder.UseSetting("ConnectionStrings:Hangfire", host.ConnectionString);
            builder.UseSetting("KeycloakSettings:Issuer", identity.Issuer);
            builder.UseSetting("KeycloakSettings:RequireHttpsMetadata", "false");
            builder.UseSetting("KeycloakSettings:ClientIds:0", TestIdentity.ClientId);
            builder.UseSetting("BackgroundJobs:Enabled", "false");
            builder.UseSetting("Operations:PublicOrigin", origin);
        });
        worker.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        using var started = worker.CreateClient();

        await AssertLoginAsync(identity, origin, username, expected,
            "/hangfire", "Background Jobs", "/hangfire/jobs/enqueued");
    }

    private static async Task AssertLoginAsync(TestIdentity identity, string origin, string username,
        HttpStatusCode expected, string path, string title, string dataPath)
    {
        using var transport = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer()
        };
        using var client = new HttpClient(transport) { BaseAddress = new Uri(origin) };
        using var challenge = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        using var login = await FollowRedirectsAsync(client, transport, challenge.Headers.Location!);
        var html = await login.Content.ReadAsStringAsync();
        var error = Regex.Match(html, "<div id=\"kc-error-message\">(.*?)</div>",
            RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Groups[1].Value;
        Assert.True(login.IsSuccessStatusCode, "Login failed: " + Regex.Replace(error, "<[^>]+>", "", RegexOptions.None, TimeSpan.FromSeconds(1)));
        var form = Regex.Match(html, "<form\\b[^>]*\\bid=\"kc-form-login\"[^>]*\\baction=\"([^\"]+)\"",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Assert.True(form.Success, "The actual identity provider must render a login form.");
        using var credentials = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = identity.Password
        });
        using var signedIn = await client.PostAsync(WebUtility.HtmlDecode(form.Groups[1].Value), credentials);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(origin + "/signin-oidc", signedIn.Headers.Location!.GetLeftPart(UriPartial.Path));
        using var callback = await client.GetAsync(signedIn.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        using var dashboard = await client.GetAsync(path);
        Assert.Equal(expected, dashboard.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Contains(title, await dashboard.Content.ReadAsStringAsync());
            using var data = await client.GetAsync(dataPath);
            Assert.Equal(HttpStatusCode.OK, data.StatusCode);
        }
        else
        {
            Assert.Equal(origin + "/Account/AccessDenied", dashboard.Headers.Location!.GetLeftPart(UriPartial.Path));
            using var denied = await client.GetAsync(dashboard.Headers.Location);
            Assert.Contains("Access Denied", await denied.Content.ReadAsStringAsync());
        }
    }

    private static int FreePort()
    {
        // Determine the loopback port before creating the realm's redirect allowlist.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<HttpResponseMessage> FollowRedirectsAsync(HttpClient client, HttpClientHandler transport, Uri address)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            var response = await client.GetAsync(address);
            // HttpClient lacks browsers' secure-cookie exception for loopback HTTP.
            Assert.True(response.RequestMessage!.RequestUri!.IsLoopback);
            foreach (Cookie cookie in transport.CookieContainer.GetAllCookies())
                cookie.Secure = false;
            if (response.StatusCode != HttpStatusCode.Redirect)
                return response;

            address = new Uri(response.RequestMessage!.RequestUri!, response.Headers.Location!);
            response.Dispose();
        }

        throw new InvalidOperationException("The login flow exceeded its redirect bound.");
    }
}
