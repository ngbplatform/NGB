using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

internal static class AgencyBillingHostFactory
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
                    .AddNgbPostgres(connectionString)
                    .AddAgencyBillingModule()
                    .AddAgencyBillingRuntimeModule()
                    .AddAgencyBillingPostgresModule();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }
}
