using NGB.Trade.Contracts;

namespace NGB.Trade.Runtime;

/// <summary>
/// Idempotent initializer for Trade defaults.
/// </summary>
public interface ITradeSetupService
{
    Task<TradeSetupResult> EnsureDefaultsAsync(CancellationToken ct = default);
}
