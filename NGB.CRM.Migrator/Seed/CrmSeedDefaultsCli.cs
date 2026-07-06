using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.CRM.DependencyInjection;
using NGB.CRM.PostgreSql.DependencyInjection;
using NGB.CRM.Runtime;
using NGB.CRM.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

namespace NGB.CRM.Migrator.Seed;

internal static class CrmSeedDefaultsCli
{
    private const string CommandName = "seed-defaults";

    public static bool IsSeedDefaultsCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static string[] TrimCommand(string[] args) => args.Length <= 1 ? [] : args[1..];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var connectionString = CrmSeedCliArgs.RequireConnectionString(args);
            var services = CreateServices(connectionString);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await using var scope = provider.CreateAsyncScope();
            var setupService = scope.ServiceProvider.GetRequiredService<ICrmSetupService>();
            var result = await setupService.EnsureDefaultsAsync();

            Console.WriteLine("OK: CRM defaults ensured.");
            Console.WriteLine($"- Opportunity stages ensured: {result.OpportunityStagesEnsured}");
            Console.WriteLine($"- Products ensured: {result.ProductsEnsured}");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: CRM default setup error.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    internal static ServiceCollection CreateServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        services
            .AddNgbRuntime()
            .AddNgbPostgres(connectionString)
            .AddCrmModule()
            .AddCrmRuntimeModule()
            .AddCrmPostgresModule();

        return services;
    }
}
