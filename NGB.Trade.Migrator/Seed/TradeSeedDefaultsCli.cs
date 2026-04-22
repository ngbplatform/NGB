using Microsoft.Extensions.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Trade.DependencyInjection;
using NGB.Trade.PostgreSql.DependencyInjection;
using NGB.Trade.Runtime;
using NGB.Trade.Runtime.DependencyInjection;

namespace NGB.Trade.Migrator.Seed;

internal static class TradeSeedDefaultsCli
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
                .AddTradeModule()
                .AddTradeRuntimeModule()
                .AddTradePostgresModule();

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await using var scope = provider.CreateAsyncScope();
            var setupService = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
            var result = await setupService.EnsureDefaultsAsync();

            Console.WriteLine("OK: trade defaults ensured.");
            Console.WriteLine($"- CoA / 1000 Operating Cash: id={result.CashAccountId}, created={result.CreatedCashAccount}");
            Console.WriteLine($"- CoA / 1100 Accounts Receivable: id={result.AccountsReceivableAccountId}, created={result.CreatedAccountsReceivableAccount}");
            Console.WriteLine($"- CoA / 1200 Inventory: id={result.InventoryAccountId}, created={result.CreatedInventoryAccount}");
            Console.WriteLine($"- CoA / 2000 Accounts Payable: id={result.AccountsPayableAccountId}, created={result.CreatedAccountsPayableAccount}");
            Console.WriteLine($"- CoA / 4000 Sales Revenue: id={result.SalesRevenueAccountId}, created={result.CreatedSalesRevenueAccount}");
            Console.WriteLine($"- CoA / 5000 Cost of Goods Sold: id={result.CostOfGoodsSoldAccountId}, created={result.CreatedCostOfGoodsSoldAccount}");
            Console.WriteLine($"- CoA / 5200 Inventory Adjustment Expense / Gain-Loss: id={result.InventoryAdjustmentAccountId}, created={result.CreatedInventoryAdjustmentAccount}");
            Console.WriteLine($"- Operational Register / trd.inventory_movements: id={result.InventoryMovementsOperationalRegisterId}, created={result.CreatedInventoryMovementsOperationalRegister}");
            Console.WriteLine($"- Reference Register / trd.item_prices: id={result.ItemPricesReferenceRegisterId}, created={result.CreatedItemPricesReferenceRegister}");
            Console.WriteLine($"- Catalog / trd.accounting_policy: id={result.AccountingPolicyCatalogId}, created={result.CreatedAccountingPolicy}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: trade default setup error.");
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
