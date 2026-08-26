using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace NGB.Api.Sso;

public sealed class MemoryCacheTicketStore : ITicketStore, IDisposable
{
    private const string KeyPrefix = "AuthSessionStore-";
    private const int DefaultMaximumSessionCount = 10_000;

    private readonly MemoryCache _cache;
    private readonly object _writeGate = new();
    private readonly int _maximumSessionCount;
    private readonly LinkedList<string> _recency = [];
    private readonly Dictionary<string, LinkedListNode<string>> _recencyNodes = new(StringComparer.Ordinal);

    public MemoryCacheTicketStore()
        : this(DefaultMaximumSessionCount)
    {
    }

    public MemoryCacheTicketStore(int maximumSessionCount)
    {
        if (maximumSessionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSessionCount));

        _maximumSessionCount = maximumSessionCount;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = maximumSessionCount
        });
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var guid = Guid.CreateVersion7();
        var key = KeyPrefix + guid;
        await RenewAsync(key, ticket);
        
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var options = new MemoryCacheEntryOptions();
        
        var expiresUtc = ticket.Properties.ExpiresUtc;
        if (expiresUtc.HasValue)
            options.SetAbsoluteExpiration(expiresUtc.Value);
        
        options.SetSlidingExpiration(TimeSpan.FromHours(1));
        options.SetSize(1);

        lock (_writeGate)
        {
            if (_recencyNodes.Remove(key, out var existingNode))
            {
                _recency.Remove(existingNode);
            }
            else
            {
                while (_recencyNodes.Count >= _maximumSessionCount)
                {
                    RemoveLeastRecentlyUsed();
                }
            }

            _cache.Set(key, ticket, options);
            _recencyNodes[key] = _recency.AddLast(key);
        }
        
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        lock (_writeGate)
        {
            if (!_cache.TryGetValue(key, out AuthenticationTicket? ticket))
            {
                RemoveRecencyNode(key);
                return Task.FromResult<AuthenticationTicket?>(null);
            }

            if (_recencyNodes.Remove(key, out var node))
            {
                _recency.Remove(node);
                _recencyNodes[key] = _recency.AddLast(key);
            }

            return Task.FromResult(ticket);
        }
    }

    public Task RemoveAsync(string key)
    {
        lock (_writeGate)
        {
            _cache.Remove(key);
            RemoveRecencyNode(key);
        }
        return Task.CompletedTask;
    }

    private void RemoveLeastRecentlyUsed()
    {
        var node = _recency.First;
        if (node is null)
            return;

        _recency.RemoveFirst();
        _recencyNodes.Remove(node.Value);
        _cache.Remove(node.Value);
    }

    private void RemoveRecencyNode(string key)
    {
        if (!_recencyNodes.Remove(key, out var node))
            return;

        _recency.Remove(node);
    }

    public void Dispose() => _cache.Dispose();
}
