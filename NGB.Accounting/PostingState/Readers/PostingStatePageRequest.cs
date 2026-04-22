using NGB.Core.Base.Paging;

namespace NGB.Accounting.PostingState.Readers;

public sealed class PostingStatePageRequest : PageSizeBase
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public PostingStateCursor? Cursor { get; init; }
    public Guid? DocumentId { get; init; }
    public PostingOperation? Operation { get; init; }
    public PostingStateStatus? Status { get; init; }
    public TimeSpan? StaleAfter { get; set; }
}
