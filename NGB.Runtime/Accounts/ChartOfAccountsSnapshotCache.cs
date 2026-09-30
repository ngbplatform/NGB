using NGB.Accounting.Accounts;

namespace NGB.Runtime.Accounts;

/// <summary>
/// Shares the immutable runtime Chart of Accounts snapshot between request scopes.
/// A scoped provider still pins the first snapshot it observes, while successful
/// management mutations invalidate the shared snapshot for newly created scopes.
/// </summary>
public sealed class ChartOfAccountsSnapshotCache
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly Lock _stateGate = new();
    private ChartOfAccounts? _snapshot;
    private long _generation;

    public async Task<ChartOfAccounts> GetOrLoadAsync(
        Func<CancellationToken, Task<ChartOfAccounts>> loader,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loader);

        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null)
            return snapshot;

        await _loadGate.WaitAsync(ct);
        try
        {
            snapshot = Volatile.Read(ref _snapshot);
            if (snapshot is not null)
                return snapshot;

            long generation;
            lock (_stateGate)
                generation = _generation;

            snapshot = await loader(ct);

            // A management mutation may commit while the database snapshot is being
            // loaded. Do not publish that potentially stale value to later scopes.
            lock (_stateGate)
            {
                if (generation == _generation)
                    Volatile.Write(ref _snapshot, snapshot);
            }

            return snapshot;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_stateGate)
        {
            _generation++;
            Volatile.Write(ref _snapshot, null);
        }
    }
}
