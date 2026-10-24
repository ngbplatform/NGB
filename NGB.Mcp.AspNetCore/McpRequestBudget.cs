using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace NGB.Mcp.AspNetCore;

internal sealed class McpRequestBudget : IDisposable
{
    internal ConcurrencyLimiter Concurrency { get; }
    internal TokenBucketRateLimiter Rate { get; }

    public McpRequestBudget(IOptions<NgbMcpOptions> options)
    {
        Concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.Value.ConcurrentRequests,
            QueueLimit = 0
        });

        Rate = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.Value.RequestsPerMinute,
            TokensPerPeriod = options.Value.RequestsPerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            AutoReplenishment = true,
            QueueLimit = 0
        });
    }

    public void Dispose()
    {
        Concurrency.Dispose();
        Rate.Dispose();
    }
}
