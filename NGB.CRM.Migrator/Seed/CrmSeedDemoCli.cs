using Microsoft.Extensions.DependencyInjection;
using NGB.CRM.Runtime;

namespace NGB.CRM.Migrator.Seed;

internal static class CrmSeedDemoCli
{
    private const string CommandName = "seed-demo";

    public static bool IsSeedDemoCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static string[] TrimCommand(string[] args) => args.Length <= 1 ? [] : args[1..];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var connectionString = CrmSeedCliArgs.RequireConnectionString(args);
            var services = CrmSeedDefaultsCli.CreateServices(connectionString);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await using var scope = provider.CreateAsyncScope();
            var seedService = scope.ServiceProvider.GetRequiredService<ICrmDemoSeedService>();
            var result = await seedService.EnsureDemoAsync();

            Console.WriteLine("OK: CRM demo seed ensured.");
            Console.WriteLine($"- As of UTC: {result.AsOfUtc:yyyy-MM-dd}");
            Console.WriteLine($"- Accounts ensured: {result.AccountsEnsured}");
            Console.WriteLine($"- Contacts ensured: {result.ContactsEnsured}");
            Console.WriteLine($"- Products ensured: {result.ProductsEnsured}");
            Console.WriteLine($"- Stages ensured: {result.StagesEnsured}");
            Console.WriteLine($"- Documents created: {result.DocumentsCreated}");
            Console.WriteLine($"- Operational data seeded: {result.SeededOperationalData}");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: CRM seed-demo error.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
