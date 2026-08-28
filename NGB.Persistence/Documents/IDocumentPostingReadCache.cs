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
}
