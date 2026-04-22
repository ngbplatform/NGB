using NGB.Accounting.Accounts;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only validator: same as <see cref="BasicAccountingPostingValidator" />,
/// but without the "same UTC day" restriction.
/// </summary>
internal sealed class RelaxedAccountingPostingValidator : IAccountingPostingValidator
{
    public void Validate(IReadOnlyList<AccountingEntry> entries)
    {
        if (entries is null)
            throw new NgbArgumentRequiredException(nameof(entries));

        var list = entries;

        if (list.Count == 0)
            throw new NotSupportedException("Document has no accounting entries");

        var documentId = list[0].DocumentId;
        var firstPeriod = list[0].Period;

        try
        {
            firstPeriod.EnsureUtc(nameof(firstPeriod));
        }
        catch (NgbArgumentInvalidException)
        {
            throw new NotSupportedException("Posting period must be UTC (DateTimeKind.Utc).");
        }

        foreach (var e in list)
        {
            if (e.DocumentId != documentId)
                throw new NotSupportedException("Entries belong to different documents");

            try
            {
                e.Period.EnsureUtc(nameof(e.Period));
            }
            catch (NgbArgumentInvalidException)
            {
                throw new NotSupportedException("Posting period must be UTC (DateTimeKind.Utc).");
            }

            ValidateEntry(e, documentId);
        }
    }

    private static void ValidateEntry(AccountingEntry e, Guid documentId)
    {
        if (e.Debit is null)
            throw new NotSupportedException("Debit account is required");

        if (e.Credit is null)
            throw new NotSupportedException("Credit account is required");

        if (e.Amount <= 0)
            throw new NotSupportedException($"Amount must be positive (Document={documentId})");

        if (string.Equals(e.Debit.Code, e.Credit.Code, StringComparison.Ordinal))
            throw new NotSupportedException($"Debit and Credit accounts must be different (Document={documentId})");

        ValidateSide(account: e.Debit, bag: e.DebitDimensions, side: "Debit", documentId: documentId);
        ValidateSide(account: e.Credit, bag: e.CreditDimensions, side: "Credit", documentId: documentId);
    }

    private static void ValidateSide(Account account, DimensionBag bag, string side, Guid documentId)
    {
        if (account is null)
            throw new NgbArgumentRequiredException(nameof(account));
        
        bag ??= DimensionBag.Empty;

        if (account.DimensionRules.Count == 0)
        {
            if (!bag.IsEmpty)
                throw new NotSupportedException($"{side}: Account '{account.Code}' does not allow analytical dimensions (Document={documentId})");

            return;
        }

        var allowed = account.DimensionRules.ToDictionary(r => r.DimensionId, r => r);

        foreach (var x in bag.Items)
        {
            if (!allowed.ContainsKey(x.DimensionId))
                throw new NotSupportedException($"{side}: Account '{account.Code}' does not allow DimensionId '{x.DimensionId}' (Document={documentId})");
        }

        foreach (var rule in account.DimensionRules)
        {
            if (!rule.IsRequired)
                continue;

            if (!ContainsDimension(bag, rule.DimensionId))
                throw new NotSupportedException($"{side}: Account '{account.Code}' requires dimension '{rule.DimensionCode}' (Document={documentId})");
        }
    }

    private static bool ContainsDimension(DimensionBag bag, Guid dimensionId)
    {
        foreach (var x in bag.Items)
        {
            if (x.DimensionId == dimensionId)
                return true;
        }

        return false;
    }
}