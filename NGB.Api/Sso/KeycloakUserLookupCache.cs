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
    private readonly Dictionary<string, HashSet<string>> _keysByUserId = new(StringComparer.Ordinal);
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

    public bool TryGetById(string userId, out IdentityProviderUserDto? user) => TryGet(IdKey(userId), out user);

    public bool TryGetByEmail(string email, out IdentityProviderUserDto? user) => TryGet(EmailKey(email), out user);

    public void Remember(IdentityProviderUserDto user)
    {
        Store(IdKey(user.UserId), user);

        if (!string.IsNullOrWhiteSpace(user.Email))
            Store(EmailKey(user.Email), user);
    }

    public void InvalidateUser(string userId, string? email = null)
    {
        var normalizedUserId = NormalizeUserId(userId);
        lock (_orderSync)
        {
            var keys = _keysByUserId.TryGetValue(normalizedUserId, out var aliases)
                ? aliases.ToArray()
                : [];

            foreach (var key in keys)
            {
                RemoveLocked(key);
            }

            // Negative id/email entries are not present in the reverse alias index.
            RemoveLocked(IdKey(normalizedUserId));
            if (!string.IsNullOrWhiteSpace(email))
                RemoveLocked(EmailKey(email));
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
            if (_entries.TryGetValue(key, out var previous))
                UnlinkUserAlias(key, previous.User);

            _entries[key] = entry;
            LinkUserAlias(key, user);

            if (_orderNodes.Remove(key, out var existingNode))
                _insertionOrder.Remove(existingNode);

            _orderNodes[key] = _insertionOrder.AddLast(key);

            while (_orderNodes.Count > maxEntries)
            {
                var oldestNode = _insertionOrder.First!;
                var oldestKey = oldestNode.Value;
                _insertionOrder.RemoveFirst();
                _orderNodes.Remove(oldestKey);

                if (_entries.TryRemove(oldestKey, out var removed))
                    UnlinkUserAlias(oldestKey, removed.User);
            }
        }
    }

    private void Remove(string key, long? expectedVersion = null)
    {
        lock (_orderSync)
        {
            if (!_entries.TryGetValue(key, out var entry) || (expectedVersion.HasValue && entry.Version != expectedVersion.Value))
                return;

            RemoveLocked(key, entry);
        }
    }

    private void RemoveLocked(string key, CacheEntry? knownEntry = null)
    {
        var entry = knownEntry;
        if (entry is null && !_entries.TryGetValue(key, out entry))
            return;

        if (!_entries.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry)))
            return;

        if (_orderNodes.Remove(key, out var node))
            _insertionOrder.Remove(node);

        UnlinkUserAlias(key, entry.User);
    }

    private void LinkUserAlias(string key, IdentityProviderUserDto? user)
    {
        if (user is null)
            return;

        var userId = NormalizeUserId(user.UserId);
        if (!_keysByUserId.TryGetValue(userId, out var aliases))
            _keysByUserId[userId] = aliases = new HashSet<string>(StringComparer.Ordinal);

        aliases.Add(key);
    }

    private void UnlinkUserAlias(string key, IdentityProviderUserDto? user)
    {
        if (user is null)
            return;

        var userId = NormalizeUserId(user.UserId);
        if (!_keysByUserId.TryGetValue(userId, out var aliases))
            return;

        aliases.Remove(key);
        if (aliases.Count == 0)
            _keysByUserId.Remove(userId);
    }

    private static string IdKey(string userId) => $"id:{userId.Trim()}";

    private static string NormalizeUserId(string userId) => userId.Trim();

    private static string EmailKey(string email) => $"email:{email.Trim().ToLowerInvariant()}";

    private sealed record CacheEntry(IdentityProviderUserDto? User, DateTimeOffset ExpiresAtUtc, long Version);
}
