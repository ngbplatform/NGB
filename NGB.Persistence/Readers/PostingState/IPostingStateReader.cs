using NGB.Accounting.PostingState.Readers;

namespace NGB.Persistence.Readers.PostingState;

/// <summary>
/// Read-side access to accounting_posting_state for operational reporting (completed/in-progress/stale).
/// Uses keyset pagination ordered by StartedAtUtc DESC.
/// </summary>
public interface IPostingStateReader
{
    Task<PostingStatePage> GetPageAsync(PostingStatePageRequest request, CancellationToken ct = default);
}
