using NGB.PropertyManagement.Contracts.Payables;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Payables;

internal static class PayablesFifoAllocator
{
    public static PayablesFifoAllocationPlan Allocate(
        IReadOnlyList<PayablesOpenChargeItemDetailsDto> charges,
        IReadOnlyList<PayablesOpenCreditItemDetailsDto> credits,
        int? limit)
    {
        if (limit is not null && limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive when specified.");

        var orderedCharges = charges
            .Where(x => x.OutstandingAmount > 0m)
            .OrderBy(x => x.DueOnUtc)
            .ThenBy(x => x.ChargeDocumentId)
            .ToArray();

        var orderedCredits = credits
            .Where(x => x.AvailableCredit > 0m)
            .OrderBy(x => x.CreditDocumentDateUtc)
            .ThenBy(x => x.CreditDocumentId)
            .ToArray();

        var outstanding = orderedCharges.ToDictionary(x => x.ChargeDocumentId, x => x.OutstandingAmount);
        var remainingCredit = orderedCredits.ToDictionary(x => x.CreditDocumentId, x => x.AvailableCredit);
        var lines = new List<PayablesFifoAllocationLine>();
        var limitReached = false;

        foreach (var cr in orderedCredits)
        {
            var creditLeft = remainingCredit[cr.CreditDocumentId];
            if (creditLeft <= 0m) continue;

            foreach (var ch in orderedCharges)
            {
                if (creditLeft <= 0m)
                    break;

                if (limit is not null && lines.Count >= limit.Value)
                {
                    limitReached = true;
                    break;
                }

                var chLeft = outstanding[ch.ChargeDocumentId];
                if (chLeft <= 0m)
                    continue;

                var amount = Math.Min(chLeft, creditLeft);
                if (amount <= 0m)
                    continue;

                var creditBefore = creditLeft;
                var chargeBefore = chLeft;
                creditLeft -= amount;
                chLeft -= amount;
                remainingCredit[cr.CreditDocumentId] = creditLeft;
                outstanding[ch.ChargeDocumentId] = chLeft;

                lines.Add(new PayablesFifoAllocationLine(
                    cr.CreditDocumentId,
                    cr.DocumentType,
                    cr.CreditDocumentDisplay,
                    cr.CreditDocumentDateUtc,
                    creditBefore,
                    creditLeft,
                    ch.ChargeDocumentId,
                    ch.ChargeDisplay,
                    ch.DueOnUtc,
                    chargeBefore,
                    chLeft,
                    amount));
            }

            if (limitReached)
                break;
        }

        return new PayablesFifoAllocationPlan(
            lines,
            outstanding.Values.Where(x => x > 0m).Sum(),
            remainingCredit.Values.Where(x => x > 0m).Sum(),
            limitReached);
    }
}

internal sealed record PayablesFifoAllocationPlan(
    IReadOnlyList<PayablesFifoAllocationLine> Lines,
    decimal RemainingOutstanding,
    decimal RemainingCredit,
    bool LimitReached);

internal sealed record PayablesFifoAllocationLine(
    Guid CreditDocumentId,
    string CreditDocumentType,
    string? CreditDocumentDisplay,
    DateOnly CreditDocumentDateUtc,
    decimal CreditAmountBefore,
    decimal CreditAmountAfter,
    Guid ChargeDocumentId,
    string? ChargeDisplay,
    DateOnly ChargeDueOnUtc,
    decimal ChargeOutstandingBefore,
    decimal ChargeOutstandingAfter,
    decimal Amount);
