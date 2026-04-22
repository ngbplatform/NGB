using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Posting;

public sealed class AccountingPostingContext(ChartOfAccounts chart) : IAccountingPostingContext
{
    private readonly List<AccountingEntry> _entries = [];

    public IReadOnlyList<AccountingEntry> Entries => _entries;

    public Task<ChartOfAccounts> GetChartOfAccountsAsync(CancellationToken ct = default)
        => Task.FromResult(chart);

    public void Post(
        Guid documentId,
        DateTime period,
        Account debit,
        Account credit,
        decimal amount,
        DimensionBag? debitDimensions = null,
        DimensionBag? creditDimensions = null,
        bool isStorno = false)
    {
        // IMPORTANT: PostingEngine integration tests assert this is a validation failure
        // and that it happens after locks are taken.
        if (documentId == Guid.Empty)
            throw new NgbArgumentOutOfRangeException(nameof(documentId), documentId, "DocumentId must be non-empty.");

        period.EnsureUtc(nameof(period));

        if (debit is null)
            throw new NgbArgumentRequiredException(nameof(debit));
        
        if (credit is null)
            throw new NgbArgumentRequiredException(nameof(credit));
        
        _entries.Add(new AccountingEntry
        {
            DocumentId = documentId,
            Period = period,
            Debit = debit,
            Credit = credit,
            Amount = amount,
            DebitDimensions = debitDimensions ?? DimensionBag.Empty,
            CreditDimensions = creditDimensions ?? DimensionBag.Empty,
            IsStorno = isStorno
        });
    }
}
