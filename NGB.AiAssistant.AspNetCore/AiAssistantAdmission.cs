using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace NGB.AiAssistant.AspNetCore;

internal sealed class AiAssistantAdmission(IOptions<AiAssistantOptions> options) : IDisposable
{
    private readonly HashSet<string> _activeUsers = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    private readonly FixedWindowRateLimiter _rate = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = options.Value.RequestsPerMinute,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    });

    public bool TryEnter(string subject)
    {
        lock (_sync)
        {
            if (_activeUsers.Count >= options.Value.ConcurrentRequests || _activeUsers.Contains(subject))
                return false;

            using var lease = _rate.AttemptAcquire();
            return lease.IsAcquired && _activeUsers.Add(subject);
        }
    }

    public void Exit(string subject)
    {
        lock (_sync)
        {
            _activeUsers.Remove(subject);
        }
    }

    public void Dispose() => _rate.Dispose();
}
