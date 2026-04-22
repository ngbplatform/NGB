using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;

namespace NGB.PropertyManagement.Runtime.Payables;

public sealed class PayablesFifoApplySuggestService(
    IPayablesOpenItemsDetailsService details,
    IDocumentDraftService drafts,
    IDocumentRelationshipService relationships,
    IPayableApplyHeadWriter applyHeadWriter,
    IUnitOfWork uow)
    : IPayablesFifoApplySuggestService
{
    private const string WarnNoCredits = "no_credits";
    private const string WarnNoCharges = "no_charges";
    private const string WarnLimitReached = "limit_reached";
    private const string WarnOutstandingRemaining = "outstanding_remaining";
    private const string WarnCreditRemaining = "credit_remaining";

    public async Task<PayablesSuggestFifoApplyResponse> SuggestAsync(
        PayablesSuggestFifoApplyRequest request,
        CancellationToken ct = default)
    {
        if (request.PartyId == Guid.Empty)
            throw PayablesRequestValidationException.VendorRequired();

        if (request.PropertyId == Guid.Empty)
            throw PayablesRequestValidationException.PropertyRequired();

        if (request.AsOfMonth is not null && request.AsOfMonth.Value.Day != 1)
            throw PayablesRequestValidationException.MonthMustBeMonthStart("asOfMonth");

        if (request.ToMonth is not null && request.ToMonth.Value.Day != 1)
            throw PayablesRequestValidationException.MonthMustBeMonthStart("toMonth");

        if (request.AsOfMonth is not null && request.ToMonth is not null && request.AsOfMonth.Value > request.ToMonth.Value)
            throw PayablesRequestValidationException.MonthRangeInvalid();

        if (request.Limit is not null && request.Limit <= 0)
            throw PayablesRequestValidationException.LimitInvalid();

        var open = await details.GetOpenItemsDetailsAsync(request.PartyId, request.PropertyId, request.AsOfMonth, request.ToMonth, ct);
        var warnings = new List<PayablesApplyWarningDto>();

        if (open.Charges.All(x => x.OutstandingAmount <= 0m))
            warnings.Add(new PayablesApplyWarningDto(WarnNoCharges, "No open charges were found for the requested range."));

        if (open.Credits.All(x => x.AvailableCredit <= 0m))
            warnings.Add(new PayablesApplyWarningDto(WarnNoCredits, "No available credits were found for the requested range."));

        var plan = PayablesFifoAllocator.Allocate(open.Charges, open.Credits, request.Limit);

        var suggested = plan.Lines
            .Select(x => new PayablesSuggestedApplyDto(
                ApplyId: null,
                CreditDocumentId: x.CreditDocumentId,
                CreditDocumentType: x.CreditDocumentType,
                CreditDocumentDisplay: x.CreditDocumentDisplay,
                CreditDocumentDateUtc: x.CreditDocumentDateUtc,
                CreditAmountBefore: x.CreditAmountBefore,
                CreditAmountAfter: x.CreditAmountAfter,
                ChargeDocumentId: x.ChargeDocumentId,
                ChargeDisplay: x.ChargeDisplay,
                ChargeDueOnUtc: x.ChargeDueOnUtc,
                ChargeOutstandingBefore: x.ChargeOutstandingBefore,
                ChargeOutstandingAfter: x.ChargeOutstandingAfter,
                Amount: x.Amount,
                ApplyPayload: PayablesApplyExecutionHelpers.BuildApplyPayload(
                    x.CreditDocumentId,
                    x.ChargeDocumentId,
                    x.CreditDocumentDateUtc,
                    x.Amount)))
            .ToList();

        var totalApplied = suggested.Sum(x => x.Amount);
        if (plan.LimitReached)
            warnings.Add(new PayablesApplyWarningDto(WarnLimitReached, "The suggestion limit was reached. Some items may remain unapplied."));

        if (plan.RemainingOutstanding > 0m)
            warnings.Add(new PayablesApplyWarningDto(WarnOutstandingRemaining, $"Outstanding charges remain: {plan.RemainingOutstanding:0.##}."));

        if (plan.RemainingCredit > 0m)
            warnings.Add(new PayablesApplyWarningDto(WarnCreditRemaining, $"Unapplied credits remain: {plan.RemainingCredit:0.##}."));

        if (request.CreateDrafts && suggested.Count > 0)
            suggested = (await CreateDraftAppliesAsync(suggested, ct)).ToList();

        return new PayablesSuggestFifoApplyResponse(
            open.RegisterId,
            open.VendorId,
            open.VendorDisplay,
            open.PropertyId,
            open.PropertyDisplay,
            open.TotalOutstanding,
            open.TotalCredit,
            totalApplied,
            plan.RemainingOutstanding,
            plan.RemainingCredit,
            suggested,
            warnings);
    }

    private async Task<IReadOnlyList<PayablesSuggestedApplyDto>> CreateDraftAppliesAsync(
        IReadOnlyList<PayablesSuggestedApplyDto> plan,
        CancellationToken ct)
    {
        var result = new List<PayablesSuggestedApplyDto>(plan.Count);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            foreach (var s in plan)
            {
                var dateUtc = DateTime.SpecifyKind(s.CreditDocumentDateUtc.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var applyId = await PayablesApplyExecutionHelpers.CreateApplyDraftAndUpsertHeadAsync(
                    drafts,
                    relationships,
                    applyHeadWriter,
                    dateUtc,
                    s.CreditDocumentId,
                    s.ChargeDocumentId,
                    s.CreditDocumentDateUtc,
                    s.Amount,
                    memo: null,
                    innerCt);

                result.Add(s with { ApplyId = applyId });
            }
        }, ct);

        return result;
    }
}
