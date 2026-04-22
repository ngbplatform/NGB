using Microsoft.Extensions.DependencyInjection;
using NGB.PropertyManagement.DependencyInjection;
using NGB.PropertyManagement.Runtime;
using NGB.PropertyManagement.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

namespace NGB.PropertyManagement.Migrator.Seed;

internal static class PropertyManagementSeedDefaultsCli
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
                .AddPropertyManagementModule()
                .AddPropertyManagementRuntimeModule()
                .AddPropertyManagementPostgresModule();

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await using var scope = provider.CreateAsyncScope();
            var setupService = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();

            var result = await setupService.EnsureDefaultsAsync();
            PrintSummary(result);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: property management default setup error.");
            Console.Error.WriteLine(ex);
            return 1;
        }
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

    private static void PrintSummary(Contracts.PropertyManagementSetupResult result)
    {
        Console.WriteLine("OK: property management defaults ensured.");
        Console.WriteLine($"- CoA / 1000 Operating Cash: id={result.CashAccountId}, created={result.CreatedCashAccount}");
        Console.WriteLine($"- Catalog / pm.bank_account (Default): id={result.DefaultBankAccountCatalogId}, created={result.CreatedDefaultBankAccount}");
        Console.WriteLine($"- CoA / 1100 Accounts Receivable - Tenants: id={result.AccountsReceivableTenantsAccountId}, created={result.CreatedAccountsReceivableTenants}");
        Console.WriteLine($"- CoA / 2000 Accounts Payable - Vendors: id={result.AccountsPayableVendorsAccountId}, created={result.CreatedAccountsPayableVendors}");
        Console.WriteLine($"- CoA / 4000 Rental Income: id={result.RentalIncomeAccountId}, created={result.CreatedRentalIncome}");
        Console.WriteLine($"- CoA / 4100 Late Fee Income: id={result.LateFeeIncomeAccountId}, created={result.CreatedLateFeeIncome}");
        Console.WriteLine($"- Operational Register / pm.tenant_balances: id={result.TenantBalancesOperationalRegisterId}, created={result.CreatedTenantBalancesOperationalRegister}");
        Console.WriteLine($"- Operational Register / pm.receivables_open_items: id={result.ReceivablesOpenItemsOperationalRegisterId}, created={result.CreatedReceivablesOpenItemsOperationalRegister}");
        Console.WriteLine($"- Operational Register / pm.payables_open_items: id={result.PayablesOpenItemsOperationalRegisterId}, created={result.CreatedPayablesOpenItemsOperationalRegister}");
        Console.WriteLine($"- Catalog / pm.accounting_policy: id={result.AccountingPolicyCatalogId}, created={result.CreatedAccountingPolicy}");
        Console.WriteLine("- Catalog / pm.receivable_charge_type defaults: Rent, Late Fee, Utility, Parking, Damage, Move out, Misc");
        Console.WriteLine("- Catalog / pm.payable_charge_type defaults: Repair, Utility, Cleaning, Supply, Misc");
        Console.WriteLine("- Catalog / pm.maintenance_category defaults: Plumbing, Electrical, HVAC, Appliance, General, Lock / Security");
    }
}
