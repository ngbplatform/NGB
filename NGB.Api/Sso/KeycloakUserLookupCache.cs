using System.Collections.Concurrent;
using NGB.Runtime.Security;

namespace NGB.Api.Sso;

/// <summary>
/// Process-wide bounded cache for Keycloak's single-user Admin API. Keycloak has no
/// multi-id endpoint, so page enrichment must reuse recent lookups and coalesce cold misses.
/// Caller cancellation only cancels that caller's wait; it never cancels a shared population.
/// </summary>
public sealed class KeycloakUserLookupCache(KeycloakAdminClientSettings settings, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<IdentityProviderUserDto?>> _pending = new(StringComparer.Ordinal);
    private readonly Lock _orderSync = new();
    private readonly LinkedList<string> _insertionOrder = [];
    private readonly Dictionary<string, LinkedListNode<string>> _orderNodes = new(StringComparer.Ordinal);
    private long _nextVersion;

    internal int InsertionMetadataCount
    {
        get
        {
            lock (_orderSync)
                return _insertionOrder.Count;
        }
    }

    public Task<IdentityProviderUserDto?> GetByIdAsync(
        string userId,
        Func<CancellationToken, Task<IdentityProviderUserDto?>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(IdKey(userId), factory, ct);

    public Task<IdentityProviderUserDto?> GetByEmailAsync(
        string email,
        Func<CancellationToken, Task<IdentityProviderUserDto?>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(EmailKey(email), factory, ct);

    public void Remember(IdentityProviderUserDto user)
    {
        Store(IdKey(user.UserId), user);

        if (!string.IsNullOrWhiteSpace(user.Email))
            Store(EmailKey(user.Email), user);
    }

    public void InvalidateUser(string userId, string? email = null)
    {
        Remove(IdKey(userId));
        if (!string.IsNullOrWhiteSpace(email))
            Remove(EmailKey(email));

        foreach (var (key, entry) in _entries)
        {
            if (entry.User is { } user && string.Equals(user.UserId, userId, StringComparison.Ordinal))
                Remove(key, entry.Version);
        }
    }

    public void InvalidateEmail(string email) => Remove(EmailKey(email));

    private async Task<IdentityProviderUserDto?> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<IdentityProviderUserDto?>> factory,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (TryGet(key, out var cached))
            return cached;

        var population = _pending.GetOrAdd(key, _ => PopulateAsync(key, factory));

        _ = population.ContinueWith(
            static (_, state) =>
            {
                var cleanup = ((KeycloakUserLookupCache Cache, string Key, Task<IdentityProviderUserDto?> Task))state!;
                cleanup.Cache._pending.TryRemove(new KeyValuePair<string, Task<IdentityProviderUserDto?>>(cleanup.Key, cleanup.Task));
            },
            (this, key, population),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return await population.WaitAsync(ct);
    }

    private async Task<IdentityProviderUserDto?> PopulateAsync(
        string key,
        Func<CancellationToken, Task<IdentityProviderUserDto?>> factory)
    {
        var user = await factory(CancellationToken.None);
        Store(key, user);

        if (user is not null)
            Remember(user);

        return user;
    }

    private bool TryGet(string key, out IdentityProviderUserDto? user)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > timeProvider.GetUtcNow())
            {
                user = entry.User;
                return true;
            }

            Remove(key, entry.Version);
        }

        user = null;
        return false;
    }

    private void Store(string key, IdentityProviderUserDto? user)
    {
        var ttl = user is null ? settings.MissingUserCacheTtl : settings.UserLookupCacheTtl;
        if (ttl <= TimeSpan.Zero)
            return;

        var entry = new CacheEntry(
            user,
            timeProvider.GetUtcNow().Add(ttl),
            Interlocked.Increment(ref _nextVersion));
        var maxEntries = Math.Clamp(settings.MaxCachedUserLookups, 100, 200_000);

        lock (_orderSync)
        {
            _entries[key] = entry;

            if (_orderNodes.Remove(key, out var existingNode))
                _insertionOrder.Remove(existingNode);

            _orderNodes[key] = _insertionOrder.AddLast(key);

            while (_orderNodes.Count > maxEntries)
            {
                var oldestNode = _insertionOrder.First!;
                var oldestKey = oldestNode.Value;
                _insertionOrder.RemoveFirst();
                _orderNodes.Remove(oldestKey);
                _entries.TryRemove(oldestKey, out _);
            }
        }
    }

    private void Remove(string key, long? expectedVersion = null)
    {
        lock (_orderSync)
        {
            if (!_entries.TryGetValue(key, out var entry) || (expectedVersion.HasValue && entry.Version != expectedVersion.Value))
                return;

            _entries.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry));
            if (_orderNodes.Remove(key, out var node))
                _insertionOrder.Remove(node);
        }
    }

    private static string IdKey(string userId) => $"id:{userId.Trim()}";

    private static string EmailKey(string email) => $"email:{email.Trim().ToLowerInvariant()}";

    private sealed record CacheEntry(IdentityProviderUserDto? User, DateTimeOffset ExpiresAtUtc, long Version);
}
