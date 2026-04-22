using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.Runtime;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

namespace NGB.AgencyBilling.Migrator.Seed;

internal static class AgencyBillingSeedDefaultsCli
{
    private const string CommandName = "seed-defaults";

    public static bool IsSeedDefaultsCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static string[] TrimCommand(string[] args) => args.Length <= 1 ? [] : args[1..];

    public static async Task<int> RunAsync(string[] args)
    {
        var connectionString = GetArgValue(args, "--connection")
            ?? Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await Console.Error.WriteLineAsync("Missing connection string. Provide --connection=\"...\" or set NGB_CONNECTION_STRING.");
            return 2;
        }

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services
                .AddNgbRuntime()
                .AddNgbPostgres(connectionString)
                .AddAgencyBillingModule()
                .AddAgencyBillingRuntimeModule()
                .AddAgencyBillingPostgresModule();

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await using var scope = provider.CreateAsyncScope();
            var setupService = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
            var result = await setupService.EnsureDefaultsAsync();

            Console.WriteLine("OK: agency billing defaults ensured.");
            Console.WriteLine($"- CoA / 1000 Operating Bank: id={result.CashAccountId}, created={result.CreatedCashAccount}");
            Console.WriteLine($"- CoA / 1100 Accounts Receivable: id={result.AccountsReceivableAccountId}, created={result.CreatedAccountsReceivableAccount}");
            Console.WriteLine($"- CoA / 4000 Service Revenue: id={result.ServiceRevenueAccountId}, created={result.CreatedServiceRevenueAccount}");
            Console.WriteLine($"- Operational Register / {AgencyBillingCodes.ProjectTimeLedgerRegisterCode}: id={result.ProjectTimeLedgerOperationalRegisterId}, created={result.CreatedProjectTimeLedgerOperationalRegister}");
            Console.WriteLine($"- Operational Register / {AgencyBillingCodes.UnbilledTimeRegisterCode}: id={result.UnbilledTimeOperationalRegisterId}, created={result.CreatedUnbilledTimeOperationalRegister}");
            Console.WriteLine($"- Operational Register / {AgencyBillingCodes.ProjectBillingStatusRegisterCode}: id={result.ProjectBillingStatusOperationalRegisterId}, created={result.CreatedProjectBillingStatusOperationalRegister}");
            Console.WriteLine($"- Operational Register / {AgencyBillingCodes.ArOpenItemsRegisterCode}: id={result.ArOpenItemsOperationalRegisterId}, created={result.CreatedArOpenItemsOperationalRegister}");
            Console.WriteLine($"- Catalog / {AgencyBillingCodes.AccountingPolicy}: id={result.AccountingPolicyCatalogId}, created={result.CreatedAccountingPolicy}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: agency billing default setup error.");
            Console.Error.WriteLine(ex);
            return 1;
        }

        return 0;
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length ? args[i + 1] : null;

            var prefix = name + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg[prefix.Length..];
        }

        return null;
    }
}
