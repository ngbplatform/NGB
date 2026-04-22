using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.GeneralJournal;

public sealed class GeneralJournalLine
{
    public long EntryId { get; init; }
    public DateTime PeriodUtc { get; init; }
    public Guid DocumentId { get; init; }

    public Guid DebitAccountId { get; init; }
    public string DebitAccountCode { get; init; } = string.Empty;

    public Guid DebitDimensionSetId { get; init; }
    public DimensionBag DebitDimensions { get; set; } = DimensionBag.Empty;
    public IReadOnlyDictionary<Guid, string> DebitDimensionValueDisplays { get; set; } =
        new Dictionary<Guid, string>();

    public Guid CreditAccountId { get; init; }
    public string CreditAccountCode { get; init; } = string.Empty;

    public Guid CreditDimensionSetId { get; init; }
    public DimensionBag CreditDimensions { get; set; } = DimensionBag.Empty;
    public IReadOnlyDictionary<Guid, string> CreditDimensionValueDisplays { get; set; } =
        new Dictionary<Guid, string>();

    public decimal Amount { get; init; }
    public bool IsStorno { get; init; }
}
