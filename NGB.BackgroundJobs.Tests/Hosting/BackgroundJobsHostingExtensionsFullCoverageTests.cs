using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.BackgroundJobs.Hosting;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Hosting;

public sealed class BackgroundJobsHostingExtensionsFullCoverageTests
{
    private const string ApplicationConnection =
        "Host=localhost;Port=5432;Database=app;Username=ngb;Password=ngb";
    private const string HangfireConnection =
        "Host=localhost;Port=5432;Database=jobs;Username=ngb;Password=ngb";

    [Fact]
    public void Add_RejectsNullBuilderAndMissingApplicationConnection()
    {
        Action nullBuilder = () => BackgroundJobsHostingExtensions.AddNgbBackgroundJobs(null!);
        nullBuilder.Should().Throw<NgbArgumentRequiredException>();

        var builder = Builder(includeApplicationConnection: false, hangfireConnection: null);
        Action missingConnection = () => builder.AddNgbBackgroundJobs();
        missingConnection.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*ConnectionStrings:DefaultConnection*");
    }

    [Fact]
    public async Task AddUseAndMap_CoverConfiguredConnectionsAuthorizationAndAccountEndpoints()
    {
        var builder = Builder(includeApplicationConnection: true, hangfireConnection: $" {HangfireConnection} ");
        var bootstrap = builder.AddNgbBackgroundJobs(options =>
        {
            options.DashboardStylesheetPaths.Clear();
            options.AdminConsoleCallbackPath = "/custom-callback";
            options.AdminConsolePublicOrigin = "https://public.example";
            options.RequireDashboardAuthorization = true;
            options.MapAccountEndpoints = true;
            options.ServerName = " server ";
            options.Queues.Clear();
            options.Queues.Add("critical");
        });

        bootstrap.ApplicationConnectionString.Should().Be(ApplicationConnection);
        bootstrap.HangfireConnectionString.Should().Be(HangfireConnection);

        await using var app = builder.Build();
        app.Services.GetRequiredService<IGlobalConfiguration>().Should().NotBeNull();
        app.UseNgbBackgroundJobs().Should().BeSameAs(app);
        app.MapNgbBackgroundJobs().Should().BeSameAs(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(x => x.RoutePattern.RawText)
            .ToArray();
        routes.Should().Contain("/health");
        routes.Should().Contain("/hangfire/{**path}");
        routes.Should().Contain("account/local-logout");

    }

    [Fact]
    public async Task AddUseAndMap_CoverFallbackConnectionAndPublicDashboardWithoutAccounts()
    {
        var builder = Builder(includeApplicationConnection: true, hangfireConnection: " ");
        var bootstrap = builder.AddNgbBackgroundJobs(options =>
        {
            options.DashboardStylesheetPaths.Clear();
            options.RequireDashboardAuthorization = false;
            options.MapAccountEndpoints = false;
        });
        bootstrap.HangfireConnectionString.Should().Be(ApplicationConnection);

        await using var app = builder.Build();
        app.Services.GetRequiredService<IGlobalConfiguration>().Should().NotBeNull();
        app.UseNgbBackgroundJobs();
        app.MapNgbBackgroundJobs();

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(x => x.RoutePattern.RawText)
            .ToArray();
        routes.Should().NotContain("account/local-logout");
    }

    [Fact]
    public void Add_CoversNullConfigureAndUseMapRejectNullApplication()
    {
        var builder = Builder(includeApplicationConnection: true, hangfireConnection: null);
        var bootstrap = builder.AddNgbBackgroundJobs();
        bootstrap.HangfireConnectionString.Should().Be(ApplicationConnection);

        Action useNull = () => BackgroundJobsHostingExtensions.UseNgbBackgroundJobs(null!);
        Action mapNull = () => BackgroundJobsHostingExtensions.MapNgbBackgroundJobs(null!);
        useNull.Should().Throw<NgbArgumentRequiredException>();
        mapNull.Should().Throw<NgbArgumentRequiredException>();
    }

    private static WebApplicationBuilder Builder(bool includeApplicationConnection, string? hangfireConnection)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Mock.Of<JobStorage>());
        var values = new Dictionary<string, string?>
        {
            ["KeycloakSettings:Issuer"] = "https://identity.example/realms/ngb",
            ["KeycloakSettings:RequireHttpsMetadata"] = bool.FalseString,
            ["KeycloakSettings:ClientIds:0"] = "ngb-background-jobs",
            ["BackgroundJobs:Enabled"] = bool.FalseString
        };
        if (includeApplicationConnection)
            values["ConnectionStrings:DefaultConnection"] = ApplicationConnection;
        if (hangfireConnection is not null)
            values["ConnectionStrings:Hangfire"] = hangfireConnection;
        builder.Configuration.AddInMemoryCollection(values);
        RegisterPlatformJobDependencyFakes(builder.Services);
        return builder;
    }

    private static void RegisterPlatformJobDependencyFakes(IServiceCollection services)
    {
        services.AddScoped(_ => Mock.Of<IDocumentsCoreSchemaValidationService>());
        services.AddScoped(_ => Mock.Of<IAccountingCoreSchemaValidationService>());
        services.AddScoped(_ => Mock.Of<IOperationalRegistersCoreSchemaValidationService>());
        services.AddScoped(_ => Mock.Of<IReferenceRegistersCoreSchemaValidationService>());
        services.AddScoped(_ => Mock.Of<ICatalogSchemaValidationService>());
        services.AddScoped(_ => Mock.Of<IDocumentSchemaValidationService>());
        services.AddScoped(_ => Mock.Of<IAccountingIntegrityChecker>());
        services.AddScoped(_ => Mock.Of<IAccountingIntegrityDiagnostics>());
        services.AddScoped(_ => Mock.Of<IUnitOfWork>());
        services.AddScoped(_ => Mock.Of<IOperationalRegisterAdminMaintenanceService>());
        services.AddScoped(_ => Mock.Of<IReferenceRegisterAdminMaintenanceService>());
        services.AddScoped(_ => Mock.Of<IPostingStateReader>());
    }
}
