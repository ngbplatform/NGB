using NGB.Core.Base.Paging;
using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.GeneralJournal;

public sealed class GeneralJournalPageRequest: PageSizeBase
{
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }
    public GeneralJournalCursor? Cursor { get; init; }
    public Guid? DocumentId { get; init; }
    public Guid? DebitAccountId { get; init; }
    public Guid? CreditAccountId { get; init; }
    public DimensionScopeBag? DimensionScopes { get; init; }
    public bool? IsStorno { get; init; }
}
