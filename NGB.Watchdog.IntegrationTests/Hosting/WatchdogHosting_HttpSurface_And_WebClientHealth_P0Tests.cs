using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NGB.Tools.Exceptions;
using NGB.Watchdog.HealthChecks;
using NGB.Watchdog.Hosting;
using Xunit;

namespace NGB.Watchdog.IntegrationTests.Hosting;

public sealed class WatchdogHosting_HttpSurface_And_WebClientHealth_P0Tests
{
    private const string TestAuthScheme = "TestAdmin";

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

    [Fact]
    public async Task Ui_RequiresInfrastructureAdminRole_WhenAuthorizationIsEnabled()
    {
        await using var target = await StartProbeServerAsync(_ => Results.Ok(new { status = "ok" }));
        await using var watchdog = await StartWatchdogAsync(
            target,
            options =>
            {
                options.RequireAuthorization = true;
                options.MapAccountEndpoints = false;
            },
            useTestAuth: true);

        using (var anonymousClient = watchdog.GetTestClient())
        {
            anonymousClient.BaseAddress = new Uri("https://localhost");
            using var response = await anonymousClient.GetAsync("/health-ui");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var applicationUserClient = watchdog.GetTestClient())
        {
            applicationUserClient.BaseAddress = new Uri("https://localhost");
            applicationUserClient.DefaultRequestHeaders.Add("X-Test-Auth", "user");
            using var response = await applicationUserClient.GetAsync("/health-ui");
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var adminClient = watchdog.GetTestClient())
        {
            adminClient.BaseAddress = new Uri("https://localhost");
            adminClient.DefaultRequestHeaders.Add("X-Test-Auth", "admin");
            using var response = await adminClient.GetAsync("/health-ui");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public void Options_normalize_paths_title_and_stylesheets()
    {
        var options = new WatchdogOptions
        {
            HealthPath = " health/ ",
            UiPath = "/",
            ApiPath = " api/// ",
            PageTitle = "  Operations  "
        };

        options.AddCustomStylesheet("  custom.css  ").Should().BeSameAs(options);
        options.ValidateAndNormalize();

        options.HealthPath.Should().Be("/health");
        options.UiPath.Should().Be("/");
        options.ApiPath.Should().Be("/api");
        options.PageTitle.Should().Be("Operations");
        options.CustomStylesheets.Should().Equal("custom.css");
    }

    [Fact]
    public void Options_reject_blank_stylesheet_paths_title_and_endpoint_paths()
    {
        ((Action)(() => new WatchdogOptions().AddCustomStylesheet(" ")))
            .Should().Throw<NgbArgumentRequiredException>();

        AssertInvalidOptions(options => options.HealthPath = null!);
        AssertInvalidOptions(options => options.UiPath = " ");
        AssertInvalidOptions(options => options.ApiPath = "");
        AssertInvalidOptions(options => options.PageTitle = " ");
    }

    [Fact]
    public void Branding_request_detection_validates_arguments_methods_and_path_boundaries()
    {
        var options = new WatchdogOptions { UiPath = "/health-ui" };
        var context = new DefaultHttpContext();

        ((Action)(() => WatchdogUiBranding.IsUiRequest(null!, options)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => WatchdogUiBranding.IsUiRequest(context.Request, null!)))
            .Should().Throw<NgbArgumentRequiredException>();

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/health-ui";
        WatchdogUiBranding.IsUiRequest(context.Request, options).Should().BeFalse();

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/different";
        WatchdogUiBranding.IsUiRequest(context.Request, options).Should().BeFalse();
        context.Request.Path = "/HEALTH-UI/details";
        WatchdogUiBranding.IsUiRequest(context.Request, options).Should().BeTrue();

        context.Request.Method = HttpMethods.Head;
        WatchdogUiBranding.IsUiRequest(context.Request, options).Should().BeTrue();
    }

    [Fact]
    public void Branding_injection_covers_blank_html_optional_favicon_and_missing_head_tag()
    {
        WatchdogUiBranding.InjectBranding(null!, "/favicon.svg").Should().BeNull();
        WatchdogUiBranding.InjectBranding(" ", "/favicon.svg").Should().Be(" ");

        var withHead = WatchdogUiBranding.InjectBranding(
            "<html><HEAD><title>Health</title></HEAD><body></body></html>",
            "/favicon.svg");
        withHead.Should().Contain("ngb-watchdog-dashboard-favicon");
        withHead.Should().Contain("/favicon.svg");
        withHead.IndexOf("ngb-watchdog-dashboard-favicon", StringComparison.Ordinal)
            .Should().BeLessThan(withHead.IndexOf("</HEAD>", StringComparison.OrdinalIgnoreCase));

        var withoutHead = WatchdogUiBranding.InjectBranding("<body>Health</body>", " ");
        withoutHead.Should().EndWith("<body>Health</body>");
        withoutHead.Should().NotContain("ngb-watchdog-dashboard-favicon");
    }

    [Fact]
    public async Task Branding_interceptor_validates_required_arguments()
    {
        var context = new DefaultHttpContext();
        var options = new WatchdogOptions();

        await ((Func<Task>)(() => WatchdogUiBranding.InterceptHtmlAsync(null!, () => Task.CompletedTask, options, "icon")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => WatchdogUiBranding.InterceptHtmlAsync(context, null!, options, "icon")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => WatchdogUiBranding.InterceptHtmlAsync(context, () => Task.CompletedTask, null!, "icon")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Branding_interceptor_bypasses_non_ui_requests()
    {
        var context = CreateBrandingContext("/health", StatusCodes.Status200OK, "text/html");
        var nextCalled = false;

        await WatchdogUiBranding.InterceptHtmlAsync(
            context,
            () =>
            {
                nextCalled = true;
                return context.Response.WriteAsync("unchanged");
            },
            new WatchdogOptions(),
            "/favicon.svg");

        nextCalled.Should().BeTrue();
        (await ReadBodyAsync(context)).Should().Be("unchanged");
    }

    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError, "text/html")]
    [InlineData(StatusCodes.Status200OK, "application/json")]
    [InlineData(StatusCodes.Status200OK, null)]
    public async Task Branding_interceptor_preserves_non_success_or_non_html_responses(int statusCode, string? contentType)
    {
        var context = CreateBrandingContext("/health-ui", statusCode, contentType);

        await WatchdogUiBranding.InterceptHtmlAsync(
            context,
            () => context.Response.WriteAsync("original"),
            new WatchdogOptions(),
            "/favicon.svg");

        (await ReadBodyAsync(context)).Should().Be("original");
    }

    [Fact]
    public async Task Branding_interceptor_injects_html_and_restores_body_when_downstream_throws()
    {
        var success = CreateBrandingContext("/health-ui", StatusCodes.Status200OK, "TEXT/HTML; charset=utf-8");
        var successBody = success.Response.Body;

        await WatchdogUiBranding.InterceptHtmlAsync(
            success,
            () => success.Response.WriteAsync("<html><head></head><body>Health</body></html>"),
            new WatchdogOptions(),
            "/favicon.svg");

        success.Response.Body.Should().BeSameAs(successBody);
        success.Response.ContentLength.Should().BeGreaterThan(0);
        (await ReadBodyAsync(success)).Should().Contain("ngb-watchdog-dashboard-favicon");

        var failed = CreateBrandingContext("/health-ui", StatusCodes.Status200OK, "text/html");
        var failedBody = failed.Response.Body;
        await ((Func<Task>)(() => WatchdogUiBranding.InterceptHtmlAsync(
                failed,
                () => throw new InvalidOperationException("downstream"),
                new WatchdogOptions(),
                "/favicon.svg")))
            .Should().ThrowAsync<InvalidOperationException>();
        failed.Response.Body.Should().BeSameAs(failedBody);
    }

    [Fact]
    public void Hosting_extensions_reject_null_builder_and_application_arguments()
    {
        ((Action)(() => WatchdogHostingExtensions.AddNgbWatchdog(null!, (Action<WatchdogOptions>?)null)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => WatchdogHostingExtensions.UseNgbWatchdog(null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => WatchdogHostingExtensions.MapNgbWatchdog(null!)))
            .Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Hosting_default_overload_covers_default_and_custom_page_titles()
    {
        await using var defaultApp = BuildWatchdogApp(
            builder => builder.AddNgbWatchdog(pageTittle: null),
            Environments.Production);
        defaultApp.Services.GetRequiredService<IOptions<WatchdogOptions>>().Value.PageTitle
            .Should().Be("NGB: Health");

        await using var customApp = BuildWatchdogApp(
            builder => builder.AddNgbWatchdog("Custom Health"),
            Environments.Development);
        var options = customApp.Services.GetRequiredService<IOptions<WatchdogOptions>>().Value;
        options.PageTitle.Should().Be("Custom Health");
        options.CustomStylesheets.Should().Equal("dashboard.css");

        var settingsType = Type.GetType("HealthChecks.UI.Configuration.Settings, HealthChecks.UI", throwOnError: true)!;
        var optionsType = typeof(IOptions<>).MakeGenericType(settingsType);
        var wrappedSettings = customApp.Services.GetRequiredService(optionsType);
        var settings = optionsType.GetProperty(nameof(IOptions<object>.Value))!.GetValue(wrappedSettings)!;
        var handlerFactory = (Delegate)settingsType
            .GetProperty("ApiEndpointHttpHandler", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(settings)!;
        using var handler = (HttpClientHandler)handlerFactory.DynamicInvoke(customApp.Services)!;
        handler.ServerCertificateCustomValidationCallback!(
                new HttpRequestMessage(),
                null,
                null,
                SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Hosting_configure_callback_is_optional()
    {
        await using var app = BuildWatchdogApp(
            builder => builder.AddNgbWatchdog((Action<WatchdogOptions>?)null),
            Environments.Production);

        app.Services.GetRequiredService<IOptions<WatchdogOptions>>().Value.HealthPath
            .Should().Be("/health");
    }

    [Fact]
    public async Task Hosting_maps_existing_absolute_and_content_root_stylesheets()
    {
        var temporaryRoot = Directory.CreateTempSubdirectory("ngb-watchdog-tests-");
        try
        {
            var absolutePath = Path.Combine(temporaryRoot.FullName, "absolute.css");
            var relativePath = "relative.css";
            await File.WriteAllTextAsync(absolutePath, "body{}");
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot.FullName, relativePath), "body{}");

            await using var app = BuildWatchdogApp(
                builder => builder.AddNgbWatchdog(options =>
                {
                    options.RequireAuthorization = false;
                    options.MapAccountEndpoints = false;
                    options.AddCustomStylesheet(absolutePath);
                    options.AddCustomStylesheet(relativePath);
                }),
                Environments.Production,
                temporaryRoot.FullName);

            app.UseNgbWatchdog().Should().BeSameAs(app);
            app.MapNgbWatchdog().Should().BeSameAs(app);
        }
        finally
        {
            temporaryRoot.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("missing-relative.css")]
    public async Task Hosting_rejects_blank_and_missing_relative_stylesheets(string stylesheet)
    {
        await using var app = BuildWatchdogApp(
            builder => builder.AddNgbWatchdog(options => options.CustomStylesheets.Add(stylesheet)),
            Environments.Production);

        ((Action)(() => app.MapNgbWatchdog())).Should().Throw<NgbException>();
    }

    [Fact]
    public async Task Hosting_rejects_missing_absolute_stylesheet()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"ngb-watchdog-missing-{Guid.NewGuid():N}.css");
        await using var app = BuildWatchdogApp(
            builder => builder.AddNgbWatchdog(options => options.AddCustomStylesheet(missing)),
            Environments.Production);

        ((Action)(() => app.MapNgbWatchdog()))
            .Should().Throw<NgbConfigurationViolationException>()
            .Which.Context.Should().ContainKey("candidatePaths");
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

    private static WebApplication BuildWatchdogApp(
        Action<WebApplicationBuilder> register,
        string environmentName,
        string? contentRootPath = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = contentRootPath ?? Directory.GetCurrentDirectory()
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebClient"] = "https://web-client.test/ping",
            ["KeycloakSettings:Issuer"] = "https://example.invalid/realms/ngb",
            ["KeycloakSettings:RequireHttpsMetadata"] = bool.FalseString,
            ["KeycloakSettings:ClientIds:0"] = "ngb-watchdog-admin"
        });
        register(builder);
        return builder.Build();
    }

    private static void AssertInvalidOptions(Action<WatchdogOptions> mutate)
    {
        var options = new WatchdogOptions();
        mutate(options);

        ((Action)options.ValidateAndNormalize).Should().Throw<NgbArgumentRequiredException>();
    }

    private static DefaultHttpContext CreateBrandingContext(string path, int statusCode, string? contentType)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static async Task<WebApplication> StartWatchdogAsync(
        WebApplication target,
        Action<WatchdogOptions> configure,
        bool addWebClient = false,
        bool useTestAuth = false)
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

        if (useTestAuth)
        {
            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthScheme;
                    options.DefaultChallengeScheme = TestAuthScheme;
                    options.DefaultForbidScheme = TestAuthScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>(TestAuthScheme, _ => { });
        }

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

    private sealed class TestAdminAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Auth", out var value))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, $"test-{value}"),
                new(ClaimTypes.Name, $"Test {value}")
            };
            if (string.Equals(value, "admin", StringComparison.Ordinal))
                claims.Add(new Claim(ClaimTypes.Role, "ngb-admin"));

            var identity = new ClaimsIdentity(claims, TestAuthScheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestAuthScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
