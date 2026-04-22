using NGB.Trade.Contracts;

namespace NGB.Trade.Runtime;

public interface ITradeDemoSeedService
{
    Task<TradeDemoSeedResult> EnsureDemoAsync(CancellationToken ct = default);
}
