using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Scoped implementation used only during Post/Unpost/Repost orchestration.
/// Values are discarded at the end of the outermost lifecycle operation.
/// Failed and cancelled reads are deliberately not cached.
/// </summary>
internal sealed class DocumentPostingReadCache : IDocumentPostingReadCache
{
    private readonly Dictionary<string, CachedValue> _values = new(StringComparer.Ordinal);
    private int _scopeDepth;

    public IDisposable BeginScope()
    {
        if (_scopeDepth == 0)
            _values.Clear();

        _scopeDepth++;

        return new Scope(this);
    }

    public async Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> valueFactory,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new NgbArgumentRequiredException(nameof(key));

        ArgumentNullException.ThrowIfNull(valueFactory);

        if (_scopeDepth == 0)
            return await valueFactory(ct);

        if (_values.TryGetValue(key, out var cached))
        {
            if (cached.ValueType == typeof(T))
                return (T)cached.Value!;

            throw CreateKeyTypeMismatch<T>(key, cached);
        }

        var value = await valueFactory(ct);
        _values.Add(key, new CachedValue(typeof(T), value));

        return value;
    }

    public void Prime<T>(string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new NgbArgumentRequiredException(nameof(key));

        if (_scopeDepth == 0)
            return;

        if (_values.TryGetValue(key, out var cached))
        {
            if (cached.ValueType == typeof(T))
                return;

            throw CreateKeyTypeMismatch<T>(key, cached);
        }

        _values.Add(key, new CachedValue(typeof(T), value));
    }

    private static NgbInvariantViolationException CreateKeyTypeMismatch<T>(string key, CachedValue cached)
        => new(
            "Document posting read cache key was reused with a different value type.",
            new Dictionary<string, object?>
            {
                ["key"] = key,
                ["expectedType"] = typeof(T).FullName,
                ["actualType"] = cached.ValueType.FullName
            });

    private void EndScope()
    {
        _scopeDepth--;
        if (_scopeDepth == 0)
            _values.Clear();
    }

    private sealed class Scope(DocumentPostingReadCache owner) : IDisposable
    {
        private DocumentPostingReadCache? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            current?.EndScope();
        }
    }

    private sealed record CachedValue(Type ValueType, object? Value);
}
