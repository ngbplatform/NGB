using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Api.Models;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Api.Services;
using NGB.CRM.DependencyInjection;
using NGB.CRM.PostgreSql.DependencyInjection;
using NGB.CRM.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.Reporting.Datasets;
using NGB.Runtime.Reporting.Definitions;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Security;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

internal static class CrmHostFactory
{
    public static IHost Create(string connectionString)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services
                    .AddNgbRuntime()
                    .AddNgbRuntimeAuthorization()
                    .AddNgbPostgres(connectionString)
                    .AddCrmModule()
                    .AddCrmRuntimeModule()
                    .AddCrmPostgresModule();

                services.RemoveAll<IMainMenuContributor>();
                RemoveRegistration<INgbPermissionDefinitionSource, PlatformPermissionDefinitionSource>(services);
                RemoveRegistration<IReportDefinitionSource, AccountingLedgerAnalysisDefinitionSource>(services);
                RemoveRegistration<IReportDefinitionSource, CanonicalAccountingReportDefinitionSource>(services);
                RemoveRegistration<IReportDatasetSource, AccountingLedgerAnalysisDatasetSource>(services);
                services.TryAddEnumerable(ServiceDescriptor.Scoped<INgbPermissionDefinitionSource, CrmPlatformPermissionDefinitionSource>());
                services.TryAddSingleton(new ExternalLinksSettings(
                    HealthUiUrl: "https://localhost:7082/health-ui",
                    BackgroundJobsUiUrl: "https://localhost:7081/hangfire"));
                services.AddScoped<IMainMenuContributor, CrmMainMenuContributor>();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }

    private static void RemoveRegistration<TService, TImplementation>(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(TService)
                && descriptor.ImplementationType == typeof(TImplementation))
            {
                services.RemoveAt(i);
            }
        }
    }
}
