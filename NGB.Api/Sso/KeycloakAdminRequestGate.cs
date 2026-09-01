using System.Threading.RateLimiting;

namespace NGB.Api.Sso;

/// <summary>
/// Process-wide bulkhead for Keycloak Admin REST calls. The bounded FIFO queue prevents
/// an unavailable identity provider from turning caller concurrency into unbounded memory use.
/// </summary>
public sealed class KeycloakAdminRequestGate : IDisposable, IAsyncDisposable
{
    private readonly ConcurrencyLimiter _limiter;

    public KeycloakAdminRequestGate(KeycloakAdminClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = settings.MaxConcurrentAdminRequests,
            QueueLimit = settings.MaxQueuedAdminRequests,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    }

    public ValueTask<RateLimitLease> AcquireAsync(CancellationToken ct) => _limiter.AcquireAsync(permitCount: 1, ct);

    public void Dispose() => _limiter.Dispose();

    public ValueTask DisposeAsync() => _limiter.DisposeAsync();
}
