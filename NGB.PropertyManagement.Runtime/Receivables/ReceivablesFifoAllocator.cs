using NGB.PropertyManagement.Contracts.Receivables;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Receivables;

internal static class ReceivablesFifoAllocator
{
    public static ReceivablesFifoAllocationPlan Allocate(
        IReadOnlyList<ReceivablesOpenChargeItemDetailsDto> charges,
        IReadOnlyList<ReceivablesOpenCreditItemDetailsDto> credits,
        int? limit)
    {
        if (limit is not null && limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive when specified.");

        // Defensive: ensure stable ordering even if upstream read model changes.
        var orderedCharges = charges
            .Where(x => x.OutstandingAmount > 0m)
            .OrderBy(x => x.DueOnUtc)
            .ThenBy(x => x.ChargeDocumentId)
            .ToArray();

        var orderedCredits = credits
            .Where(x => x.AvailableCredit > 0m)
            .OrderBy(x => x.ReceivedOnUtc)
            .ThenBy(x => x.CreditDocumentId)
            .ToArray();

        var outstanding = orderedCharges.ToDictionary(x => x.ChargeDocumentId, x => x.OutstandingAmount);
        var remainingCredit = orderedCredits.ToDictionary(x => x.CreditDocumentId, x => x.AvailableCredit);

        var lines = new List<ReceivablesFifoAllocationLine>();
        var limitReached = false;

        foreach (var cr in orderedCredits)
        {
            var creditLeft = remainingCredit[cr.CreditDocumentId];
            if (creditLeft <= 0m)
                continue;

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

                lines.Add(new ReceivablesFifoAllocationLine(
                    CreditDocumentId: cr.CreditDocumentId,
                    CreditDocumentDisplay: cr.CreditDocumentDisplay,
                    CreditDocumentDateUtc: cr.ReceivedOnUtc,
                    CreditAmountBefore: creditBefore,
                    CreditAmountAfter: creditLeft,
                    ChargeDocumentId: ch.ChargeDocumentId,
                    ChargeDisplay: ch.ChargeDisplay,
                    ChargeDueOnUtc: ch.DueOnUtc,
                    ChargeOutstandingBefore: chargeBefore,
                    ChargeOutstandingAfter: chLeft,
                    Amount: amount));
            }

            if (limitReached)
                break;
        }

        var remainingOutstandingTotal = outstanding.Values.Where(x => x > 0m).Sum();
        var remainingCreditTotal = remainingCredit.Values.Where(x => x > 0m).Sum();

        return new ReceivablesFifoAllocationPlan(
            Lines: lines,
            RemainingOutstanding: remainingOutstandingTotal,
            RemainingCredit: remainingCreditTotal,
            LimitReached: limitReached);
    }
}

internal sealed record ReceivablesFifoAllocationPlan(
    IReadOnlyList<ReceivablesFifoAllocationLine> Lines,
    decimal RemainingOutstanding,
    decimal RemainingCredit,
    bool LimitReached);

internal sealed record ReceivablesFifoAllocationLine(
    Guid CreditDocumentId,
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
