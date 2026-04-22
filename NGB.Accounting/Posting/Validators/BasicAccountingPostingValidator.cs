using System.Globalization;
using NGB.Accounting.Accounts;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Accounting.Posting.Validators;

public sealed class BasicAccountingPostingValidator : IAccountingPostingValidator
{
    /// <summary>
    /// Max scale of decimal amount supported by DB.
    /// MUST match schema for accounting_register_main.amount.
    /// </summary>
    private const int MaxAmountScale = 4;

    public void Validate(IReadOnlyList<AccountingEntry> entries)
    {
        if (entries is null)
            throw new NgbArgumentRequiredException(nameof(entries));

        if (entries.Count == 0)
            throw new NgbArgumentInvalidException("entries", "Document has no accounting entries.");

        static void ThrowInvalid(string reason) => throw new NgbArgumentInvalidException("entries", reason);

        var first = entries[0];

        try
        {
            first.Period.EnsureUtc(nameof(first.Period));
        }
        catch (NgbArgumentInvalidException)
        {
            ThrowInvalid("Posting period must be UTC (DateTimeKind.Utc).");
        }

        var documentId = first.DocumentId;
        var periodUtc = first.Period;
        var dayUtc = periodUtc.Date;

        foreach (var e in entries)
        {
            if (e.DocumentId != documentId)
                ThrowInvalid($"All entries must belong to the same DocumentId. Expected {documentId}, actual {e.DocumentId}.");

            try
            {
                e.Period.EnsureUtc(nameof(e.Period));
            }
            catch (NgbArgumentInvalidException)
            {
                ThrowInvalid($"Posting period must be UTC (DateTimeKind.Utc). DocumentId={documentId}.");
            }

            if (e.Period.Date != dayUtc)
                ThrowInvalid($"All entries must be in the same UTC day. Expected {dayUtc:O}, actual {e.Period.Date:O}. DocumentId={documentId}.");

            if (e.Amount <= 0m)
                ThrowInvalid($"Entry amount must be > 0. DocumentId={documentId}.");

            // Ensure stable precision (to match DECIMAL(?, MaxAmountScale) in DB)
            var scale = GetScale(e.Amount);
            if (scale > MaxAmountScale)
                ThrowInvalid($"Entry amount has too many decimal places (scale={scale}, max={MaxAmountScale}). DocumentId={documentId}.");

            if (e.Debit is null)
                ThrowInvalid($"Debit account is required. DocumentId={documentId}.");

            if (e.Credit is null)
                ThrowInvalid($"Credit account is required. DocumentId={documentId}.");

            var debit = e.Debit!;
            var credit = e.Credit!;

            if (ReferenceEquals(debit, credit) || debit.Id == credit.Id)
                ThrowInvalid($"Debit and Credit accounts must be different. DocumentId={documentId}. AccountId={debit.Id}.");

            ValidateSide("Debit", debit, e.DebitDimensions, documentId);
            ValidateSide("Credit", credit, e.CreditDimensions, documentId);
        }

        static void ValidateSide(
            string side,
            Account account,
            DimensionBag? bag,
            Guid documentId)
        {
            if (account is null)
                throw new NgbArgumentRequiredException(nameof(account));

            bag ??= DimensionBag.Empty;

            // If account has no dimension rules, no dimensions are allowed.
            if (account.DimensionRules.Count == 0)
            {
                if (!bag.IsEmpty)
                    throw new NgbArgumentInvalidException(
                        "entries",
                        $"{side}: Account '{account.Code}' does not allow dimensions (DocumentId={documentId}).");

                return;
            }

            var allowed = new HashSet<Guid>();
            foreach (var rule in account.DimensionRules)
            {
                allowed.Add(rule.DimensionId);
            }

            foreach (var x in bag.Items)
            {
                if (!allowed.Contains(x.DimensionId))
                {
                    throw new NgbArgumentInvalidException(
                        "entries",
                        $"{side}: Account '{account.Code}' does not allow dimension '{x.DimensionId}' (DocumentId={documentId}).");
                }
            }

            foreach (var rule in account.DimensionRules)
            {
                if (!rule.IsRequired)
                    continue;

                if (!ContainsDimension(bag, rule.DimensionId))
                {
                    throw new NgbArgumentInvalidException(
                        "entries",
                        $"{side}: Account '{account.Code}' requires dimension '{rule.DimensionCode}' (DocumentId={documentId}).");
                }
            }
        }

        static bool ContainsDimension(DimensionBag bag, Guid dimensionId)
        {
            foreach (var x in bag.Items)
            {
                if (x.DimensionId == dimensionId)
                    return true;
            }

            return false;
        }

        static int GetScale(decimal value)
        {
            var s = value.ToString(CultureInfo.InvariantCulture);
            var dot = s.IndexOf('.');
            return dot < 0 ? 0 : s.Length - dot - 1;
        }
    }
}
