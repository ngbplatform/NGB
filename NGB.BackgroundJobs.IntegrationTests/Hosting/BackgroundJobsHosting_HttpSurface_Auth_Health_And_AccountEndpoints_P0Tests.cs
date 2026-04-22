using System.Net;
using System.Security.Claims;
using System.Data.Common;
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
using Microsoft.Extensions.Options;
using NGB.Accounting.PostingState.Readers;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Bootstrap;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Catalogs.Schema;
using NGB.Runtime.Documents;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using Npgsql;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hosting;

[Collection(HangfirePostgresCollection.Name)]
public sealed class BackgroundJobsHosting_HttpSurface_Auth_Health_And_AccountEndpoints_P0Tests
{
    private const string TestAuthScheme = "TestAdmin";
    private readonly HangfirePostgresFixture _fixture;

    public BackgroundJobsHosting_HttpSurface_Auth_Health_And_AccountEndpoints_P0Tests(HangfirePostgresFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Health_Dashboard_And_Account_Endpoints_Work_When_Public_Mode_Is_Enabled()
    {
        await using var applicationDb = await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "ngb_bgjobs_app");
        await using var hangfireDb = await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "ngb_bgjobs_hf");
        await DatabaseBootstrapper.InitializeAsync(applicationDb.ConnectionString);
        await DatabaseBootstrapper.RepairAsync(applicationDb.ConnectionString);

        await using var keycloak = await StartKeycloakProbeAsync();
        await using var app = await StartBackgroundJobsAsync(
            applicationDb.ConnectionString,
            hangfireDb.ConnectionString,
            keycloak,
            options =>
            {
                options.RequireDashboardAuthorization = false;
                options.MapAccountEndpoints = true;
                options.DashboardTitle = "NGB: Test Background Jobs";
                options.DashboardStylesheetPaths.Clear();
            });

        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        using (var healthResponse = await client.GetAsync("/health"))
        {
            healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var payload = await ReadJsonAsync(healthResponse);
            payload.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
            payload.RootElement.GetProperty("entries").GetProperty("PostgreSQL Server").GetProperty("status").GetString().Should().Be("Healthy");
            payload.RootElement.GetProperty("entries").GetProperty("Keycloak").GetProperty("status").GetString().Should().Be("Healthy");
            payload.RootElement.GetProperty("entries").GetProperty("Jobs").GetProperty("status").GetString().Should().Be("Healthy");
        }

        using (var dashboardResponse = await client.GetAsync("/hangfire"))
        {
            dashboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            dashboardResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

            var html = await dashboardResponse.Content.ReadAsStringAsync();
            html.Should().Contain("NGB: Test Background Jobs");
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
    public async Task Dashboard_Requires_Admin_Authentication_When_Authorization_Is_Enabled()
    {
        await using var applicationDb = await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "ngb_bgjobs_app");
        await using var hangfireDb = await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "ngb_bgjobs_hf");
        await DatabaseBootstrapper.InitializeAsync(applicationDb.ConnectionString);
        await DatabaseBootstrapper.RepairAsync(applicationDb.ConnectionString);

        await using var keycloak = await StartKeycloakProbeAsync();
        await using var app = await StartBackgroundJobsAsync(
            applicationDb.ConnectionString,
            hangfireDb.ConnectionString,
            keycloak,
            options =>
            {
                options.RequireDashboardAuthorization = true;
                options.MapAccountEndpoints = false;
                options.DashboardTitle = "NGB: Protected Jobs";
                options.DashboardStylesheetPaths.Clear();
            },
            useTestAuth: true);

        using (var anonymousClient = app.GetTestClient())
        {
            anonymousClient.BaseAddress = new Uri("https://localhost");

            using var unauthorizedResponse = await anonymousClient.GetAsync("/hangfire");
            unauthorizedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var authenticatedClient = app.GetTestClient())
        {
            authenticatedClient.BaseAddress = new Uri("https://localhost");
            authenticatedClient.DefaultRequestHeaders.Add("X-Test-Auth", "admin");

            using var dashboardResponse = await authenticatedClient.GetAsync("/hangfire");
            dashboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            dashboardResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

            var html = await dashboardResponse.Content.ReadAsStringAsync();
            html.Should().Contain("NGB: Protected Jobs");
        }
    }

    [Fact]
    public async Task Account_Endpoints_Are_Not_Mapped_When_Disabled()
    {
        await using var applicationDb = await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "ngb_bgjobs_app");
        await using var hangfireDb = await TemporaryDatabase.CreateAsync(_fixture.ConnectionString, "ngb_bgjobs_hf");
        await DatabaseBootstrapper.InitializeAsync(applicationDb.ConnectionString);
        await DatabaseBootstrapper.RepairAsync(applicationDb.ConnectionString);

        await using var keycloak = await StartKeycloakProbeAsync();
        await using var app = await StartBackgroundJobsAsync(
            applicationDb.ConnectionString,
            hangfireDb.ConnectionString,
            keycloak,
            options =>
            {
                options.RequireDashboardAuthorization = false;
                options.MapAccountEndpoints = false;
                options.DashboardStylesheetPaths.Clear();
            });

        using var client = app.GetTestClient();
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

    private static async Task<WebApplication> StartKeycloakProbeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapGet("/{**path}", () => Results.Json(new
        {
            issuer = "https://keycloak.test/realms/ngb",
            authorization_endpoint = "https://keycloak.test/realms/ngb/protocol/openid-connect/auth",
            token_endpoint = "https://keycloak.test/realms/ngb/protocol/openid-connect/token",
            jwks_uri = "https://keycloak.test/realms/ngb/protocol/openid-connect/certs"
        }));
        await app.StartAsync();

        return app;
    }

    private static async Task<WebApplication> StartBackgroundJobsAsync(
        string applicationConnectionString,
        string hangfireConnectionString,
        WebApplication keycloakProbe,
        Action<BackgroundJobsHostingOptions> configure,
        bool useTestAuth = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = applicationConnectionString,
            ["ConnectionStrings:Hangfire"] = hangfireConnectionString,
            ["KeycloakSettings:Issuer"] = "https://keycloak.test/realms/ngb",
            ["KeycloakSettings:RequireHttpsMetadata"] = bool.FalseString,
            ["KeycloakSettings:ClientIds:0"] = "ngb-admin-console",
            ["BackgroundJobs:Enabled"] = bool.FalseString,
        });

        var bootstrap = builder.AddNgbBackgroundJobs(configure);

        RegisterPlatformJobDependencyFakes(builder.Services, applicationConnectionString);

        builder.Services
            .AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => keycloakProbe.GetTestServer().CreateHandler());

        if (useTestAuth)
        {
            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthScheme;
                    options.DefaultChallengeScheme = TestAuthScheme;
                    options.DefaultForbidScheme = TestAuthScheme;
                    options.DefaultSignInScheme = TestAuthScheme;
                    options.DefaultSignOutScheme = TestAuthScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>(TestAuthScheme, _ => { });
        }

        await bootstrap.EnsureInfrastructureAsync();

        var app = builder.Build();
        app.UseNgbBackgroundJobs();
        app.MapNgbBackgroundJobs();
        await app.StartAsync();

        return app;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static void RegisterPlatformJobDependencyFakes(IServiceCollection services, string applicationConnectionString)
    {
        services.AddSingleton<NoOpSchemaValidationService>();
        services.AddSingleton<IDocumentsCoreSchemaValidationService>(sp => sp.GetRequiredService<NoOpSchemaValidationService>());
        services.AddSingleton<IAccountingCoreSchemaValidationService>(sp => sp.GetRequiredService<NoOpSchemaValidationService>());
        services.AddSingleton<IOperationalRegistersCoreSchemaValidationService>(sp => sp.GetRequiredService<NoOpSchemaValidationService>());
        services.AddSingleton<IReferenceRegistersCoreSchemaValidationService>(sp => sp.GetRequiredService<NoOpSchemaValidationService>());
        services.AddSingleton<IDocumentSchemaValidationService>(sp => sp.GetRequiredService<NoOpSchemaValidationService>());
        services.AddSingleton<ICatalogSchemaValidationService, NoOpCatalogSchemaValidationService>();
        services.AddSingleton<IAccountingIntegrityChecker, NoOpAccountingIntegrityChecker>();
        services.AddSingleton<IAccountingIntegrityDiagnostics, NoOpAccountingIntegrityDiagnostics>();
        services.AddSingleton<IOperationalRegisterAdminMaintenanceService, NoOpOperationalRegisterAdminMaintenanceService>();
        services.AddSingleton<IReferenceRegisterAdminMaintenanceService, NoOpReferenceRegisterAdminMaintenanceService>();
        services.AddSingleton<IPostingStateReader, EmptyPostingStateReader>();
        services.AddScoped<IUnitOfWork>(_ => new TestUnitOfWork(applicationConnectionString));
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

            if (!string.Equals(value.ToString(), "admin", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.Fail("Unknown test user."));

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "Test Admin"),
                    new Claim(ClaimTypes.Role, "ngb-admin")
                ],
                authenticationType: TestAuthScheme);

            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, TestAuthScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpSchemaValidationService :
        IDocumentsCoreSchemaValidationService,
        IAccountingCoreSchemaValidationService,
        IOperationalRegistersCoreSchemaValidationService,
        IReferenceRegistersCoreSchemaValidationService,
        IDocumentSchemaValidationService
    {
        public Task ValidateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ValidateAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpCatalogSchemaValidationService : ICatalogSchemaValidationService
    {
        public Task<SchemaDiagnosticsResult> DiagnoseAllAsync(CancellationToken ct = default)
            => Task.FromResult(new SchemaDiagnosticsResult());

        public Task ValidateAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpAccountingIntegrityChecker : IAccountingIntegrityChecker
    {
        public Task AssertPeriodIsBalancedAsync(DateOnly period, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpAccountingIntegrityDiagnostics : IAccountingIntegrityDiagnostics
    {
        public Task<long> GetTurnoversVsRegisterDiffCountAsync(DateOnly period, CancellationToken ct = default)
            => Task.FromResult(0L);
    }

    private sealed class NoOpOperationalRegisterAdminMaintenanceService : IOperationalRegisterAdminMaintenanceService
    {
        public Task<OperationalRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(
            Guid registerId,
            CancellationToken ct = default)
            => Task.FromResult<OperationalRegisterPhysicalSchemaHealth?>(null);

        public Task<OperationalRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(CancellationToken ct = default)
            => Task.FromResult(new OperationalRegisterPhysicalSchemaHealthReport([]));

        public Task MarkFinalizationDirtyAsync(Guid registerId, DateOnly periodMonth, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> FinalizeDirtyAsync(int maxItems = 50, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> FinalizeRegisterDirtyAsync(Guid registerId, int maxPeriods = 50, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class NoOpReferenceRegisterAdminMaintenanceService : IReferenceRegisterAdminMaintenanceService
    {
        public Task<ReferenceRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(
            Guid registerId,
            CancellationToken ct = default)
            => Task.FromResult<ReferenceRegisterPhysicalSchemaHealth?>(null);

        public Task<ReferenceRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(CancellationToken ct = default)
            => Task.FromResult(new ReferenceRegisterPhysicalSchemaHealthReport([]));
    }

    private sealed class EmptyPostingStateReader : IPostingStateReader
    {
        private static readonly PostingStatePage EmptyPage = new([], HasMore: false, NextCursor: null);

        public Task<PostingStatePage> GetPageAsync(PostingStatePageRequest request, CancellationToken ct = default)
            => Task.FromResult(EmptyPage);
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly NpgsqlConnection _connection;

        public TestUnitOfWork(string connectionString)
        {
            _connection = new NpgsqlConnection(connectionString);
        }

        public DbConnection Connection => _connection;
        public DbTransaction? Transaction { get; private set; }
        public bool HasActiveTransaction => Transaction is not null;

        public async Task EnsureConnectionOpenAsync(CancellationToken ct = default)
        {
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(ct);
        }

        public async Task BeginTransactionAsync(CancellationToken ct = default)
        {
            if (Transaction is not null)
                return;

            await EnsureConnectionOpenAsync(ct);
            Transaction = await _connection.BeginTransactionAsync(ct);
        }

        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (Transaction is null)
                return;

            await Transaction.CommitAsync(ct);
            await Transaction.DisposeAsync();
            Transaction = null;
        }

        public async Task RollbackAsync(CancellationToken ct = default)
        {
            if (Transaction is null)
                return;

            await Transaction.RollbackAsync(ct);
            await Transaction.DisposeAsync();
            Transaction = null;
        }

        public void EnsureActiveTransaction()
        {
            if (Transaction is null)
                throw new InvalidOperationException("Active transaction is required.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Transaction is not null)
                await Transaction.DisposeAsync();

            await _connection.DisposeAsync();
        }
    }
}
