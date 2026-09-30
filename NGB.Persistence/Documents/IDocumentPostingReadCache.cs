using NGB.Core.Documents;

namespace NGB.Persistence.Documents;

/// <summary>
/// Operation-local cache for immutable reads performed while a document lifecycle
/// transition is being built. Outside an explicit scope every read goes directly
/// to persistence, so normal request-scoped services never retain stale document data.
/// </summary>
public interface IDocumentPostingReadCache
{
    IDisposable BeginScope();

    Task<T> GetOrAddAsync<T>(string key, Func<CancellationToken, Task<T>> valueFactory, CancellationToken ct = default);

    /// <summary>
    /// Seeds a value loaded by a batch prefetcher. Implementations that do not support
    /// priming may keep the default no-op behavior without changing lifecycle semantics.
    /// </summary>
    void Prime<T>(string key, T value)
    {
    }
}

/// <summary>
/// Provider-specific hook that can preload typed data for an atomic posting batch.
/// Implementations must use the current UnitOfWork/transaction and only prime immutable
/// posting reads; applying business effects remains ordered in the Runtime orchestrator.
/// </summary>
public interface IDocumentPostingBatchReadPrefetcher
{
    Task PrefetchAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken ct = default);
}
