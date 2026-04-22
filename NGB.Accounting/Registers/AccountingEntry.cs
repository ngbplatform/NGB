using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Tools.Extensions;

namespace NGB.Accounting.Registers;

/// <summary>
/// A single accounting transaction line in the main register:
/// Debit / Credit accounts with an amount plus analytical dimensions.
/// </summary>
public sealed class AccountingEntry
{
    private DateTime _periodUtc;

    public long EntryId { get; init; }

    public Guid DocumentId { get; init; }

    /// <summary>
    /// Posting timestamp in UTC.
    /// IMPORTANT: This must always be <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    public DateTime Period
    {
        get => _periodUtc;
        init
        {
            value.EnsureUtc(nameof(Period));
            _periodUtc = value;
        }
    }

    public Account Debit { get; init; } = null!;
    public Account Credit { get; init; } = null!;

    public decimal Amount { get; init; }

    public bool IsStorno { get; init; }

    /// <summary>
    /// Debit-side analytical dimensions for this entry.
    /// </summary>
    public DimensionBag DebitDimensions { get; init; } = DimensionBag.Empty;

    /// <summary>
    /// Credit-side analytical dimensions for this entry.
    /// </summary>
    public DimensionBag CreditDimensions { get; init; } = DimensionBag.Empty;

    /// <summary>
    /// Analytical dimensions (Dimension Set ID) for the debit side.
    ///
    /// For an empty dimensions bag this is <see cref="Guid.Empty"/>.
    /// This value is resolved by the PostingEngine and persisted into the register.
    /// Posting handlers may also set it explicitly when the set id is already known.
    /// </summary>
    public Guid DebitDimensionSetId { get; set; }

    /// <summary>
    /// Analytical dimensions (Dimension Set ID) for the credit side.
    ///
    /// For an empty dimensions bag this is <see cref="Guid.Empty"/>.
    /// This value is resolved by the PostingEngine and persisted into the register.
    /// Posting handlers may also set it explicitly when the set id is already known.
    /// </summary>
    public Guid CreditDimensionSetId { get; set; }
}
