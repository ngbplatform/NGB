using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Runtime-level posting log report (validation + defaults).
/// </summary>
internal sealed class PostingStateReportService(IPostingStateReader reader) : IPostingStateReportReader
{
    public Task<PostingStatePage> GetPageAsync(PostingStatePageRequest request, CancellationToken ct = default)
    {
        // Runtime contract: strict UTC bounds if provided + safe defaults.
        _ = request.NormalizeForQuery(
            PostingStatePageRequestNormalization.UtcBoundsPolicy.StrictUtc,
            PostingStatePageRequestNormalization.BoundsValidationMode.BothExplicit);

        return reader.GetPageAsync(request, ct);
    }
}
