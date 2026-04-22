using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Trade.DependencyInjection;
using NGB.Trade.PostgreSql.DependencyInjection;
using NGB.Trade.Runtime.DependencyInjection;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

internal static class TradeHostFactory
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
                    .AddTradeModule()
                    .AddTradeRuntimeModule()
                    .AddTradePostgresModule();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }
}
