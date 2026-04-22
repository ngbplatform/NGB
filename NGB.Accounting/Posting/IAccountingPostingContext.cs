using NGB.Accounting.Accounts;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;

namespace NGB.Accounting.Posting;

public interface IAccountingPostingContext
{
    IReadOnlyList<AccountingEntry> Entries { get; }

    Task<ChartOfAccounts> GetChartOfAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new accounting entry to the posting context.
    ///
    /// Dimensions:
    /// - Provide <paramref name="debitDimensions"/> and <paramref name="creditDimensions"/> as an open-ended
    ///   <see cref="DimensionBag"/> (may be empty).
    /// - If you already know a persisted DimensionSetId (e.g., from a document line), you can set
    ///   <see cref="AccountingEntry.DebitDimensionSetId"/> and <see cref="AccountingEntry.CreditDimensionSetId"/>
    ///   on the created entry after calling <c>Post</c>. PostingEngine will not overwrite non-empty IDs.
    /// </summary>
    void Post(
        Guid documentId,
        DateTime period,
        Account debit,
        Account credit,
        decimal amount,
        DimensionBag? debitDimensions = null,
        DimensionBag? creditDimensions = null,
        bool isStorno = false);
}
