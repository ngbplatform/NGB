using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;

namespace NGB.Runtime.Accounts;

public sealed class ChartOfAccountsProvider(IChartOfAccountsRepository repo) : IChartOfAccountsProvider
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private ChartOfAccounts? _cached;

    public async Task<ChartOfAccounts> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
            return _cached;

        await _semaphore.WaitAsync(ct);
        try
        {
            _cached ??= await LoadAsync(ct);
        }
        finally
        {
            _semaphore.Release();
        }

        return _cached;
    }

    private async Task<ChartOfAccounts> LoadAsync(CancellationToken ct)
    {
        var accounts = await repo.GetAllAsync(ct);
        var chart = new ChartOfAccounts();

        foreach (var a in accounts)
        {
            chart.Add(a);
        }
            
        return chart;
    }
}
