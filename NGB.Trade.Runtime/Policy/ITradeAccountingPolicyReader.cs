namespace NGB.Trade.Runtime.Policy;

public interface ITradeAccountingPolicyReader
{
    Task<TradeAccountingPolicy> GetRequiredAsync(CancellationToken ct = default);
}
