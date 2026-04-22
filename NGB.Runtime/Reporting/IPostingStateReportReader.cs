using NGB.Accounting.PostingState.Readers;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Runtime-level posting log report reader (validation + defaults).
/// Exposed as an abstraction to keep consumers provider-agnostic and concrete-free.
/// </summary>
public interface IPostingStateReportReader
{
    Task<PostingStatePage> GetPageAsync(PostingStatePageRequest request, CancellationToken ct = default);
}
