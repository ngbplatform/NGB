using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// FIFO suggestion engine for receivables applies.
///
/// Supports two use-cases:
/// - Suggest FIFO applies for a single posted payment credit (no writes).
/// - Suggest FIFO applies across a lease (UI wizard), optionally materializing draft apply documents.
/// </summary>
public sealed class ReceivablesFifoApplySuggestService(
    IReceivablesOpenItemsDetailsService details,
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IDocumentDraftService drafts,
    IDocumentRelationshipService relationships,
    IReceivableApplyHeadWriter applyHeadWriter,
    IUnitOfWork uow)
    : IReceivablesFifoApplySuggestService
{
    private const string WarnNoCredits = "no_credits";
    private const string WarnNoCharges = "no_charges";
    private const string WarnLimitReached = "limit_reached";
    private const string WarnOutstandingRemaining = "outstanding_remaining";
    private const string WarnCreditRemaining = "credit_remaining";

    /// <summary>
    /// Computes a FIFO allocation plan for a posted receivable payment credit and returns
    /// a batch of pm.receivable_apply payloads (no writes).
    /// </summary>
    public async Task<ReceivablesFifoApplySuggestResponse> SuggestAsync(
        ReceivablesFifoApplySuggestRequest request,
        CancellationToken ct = default)
    {
        if (request.CreditDocumentId == Guid.Empty)
            throw ReceivablesRequestValidationException.PaymentRequired();

        if (request.MaxApplications is not null && request.MaxApplications <= 0)
            throw ReceivablesRequestValidationException.MaxApplicationsInvalid();

        var creditSource = await uow.ExecuteInUowTransactionAsync(
            innerCt => ReceivableCreditSourceResolver.ReadRequiredAsync(readers, documents, request.CreditDocumentId, innerCt),
            ct);

        var open = await details.GetOpenItemsDetailsAsync(
            partyId: creditSource.PartyId,
            propertyId: creditSource.PropertyId,
            leaseId: creditSource.LeaseId,
            asOfMonth: null,
            toMonth: null,
            ct: ct);

        var credit = open.Credits.FirstOrDefault(x => x.CreditDocumentId == request.CreditDocumentId);
        if (credit is null || credit.AvailableCredit <= 0m)
            throw ReceivablesRequestValidationException.PaymentHasNoAvailableCredit(request.CreditDocumentId);

        var plan = ReceivablesFifoAllocator.Allocate(
            charges: open.Charges,
            credits: [credit],
            limit: request.MaxApplications);

        var suggested = plan.Lines
            .Select(x => new ReceivablesSuggestedApplyDto(
                ChargeDocumentId: x.ChargeDocumentId,
                ChargeOutstandingBefore: x.ChargeOutstandingBefore,
                ChargeDueOnUtc: x.ChargeDueOnUtc,
                Amount: x.Amount,
                ApplyPayload: ReceivablesApplyExecutionHelpers.BuildApplyPayload(
                    creditDocumentId: x.CreditDocumentId,
                    chargeDocumentId: x.ChargeDocumentId,
                    appliedOnUtc: x.CreditDocumentDateUtc,
                    amount: x.Amount)))
            .ToArray();

        var totalApplied = suggested.Sum(x => x.Amount);

        return new ReceivablesFifoApplySuggestResponse(
            CreditDocumentId: request.CreditDocumentId,
            RegisterId: open.RegisterId,
            AvailableCredit: credit.AvailableCredit,
            TotalOutstanding: open.TotalOutstanding,
            TotalApplied: totalApplied,
            RemainingCredit: plan.RemainingCredit,
            SuggestedApplies: suggested);
    }

    public async Task<ReceivablesSuggestFifoApplyResponse> SuggestLeaseAsync(
        ReceivablesSuggestFifoApplyRequest request,
        CancellationToken ct = default)
    {
        if (request.LeaseId == Guid.Empty)
            throw ReceivablesRequestValidationException.LeaseRequired();

        if (request.AsOfMonth is not null)
            if (request.AsOfMonth.Value.Day != 1)
                throw ReceivablesRequestValidationException.MonthMustBeMonthStart("asOfMonth");

        if (request.ToMonth is not null)
            if (request.ToMonth.Value.Day != 1)
                throw ReceivablesRequestValidationException.MonthMustBeMonthStart("toMonth");

        if (request.AsOfMonth is not null && request.ToMonth is not null && request.AsOfMonth.Value > request.ToMonth.Value)
            throw ReceivablesRequestValidationException.MonthRangeInvalid();

        if (request.Limit is not null && request.Limit <= 0)
            throw ReceivablesRequestValidationException.LimitInvalid();

        var open = await details.GetOpenItemsDetailsAsync(
            partyId: request.PartyId ?? Guid.Empty,
            propertyId: request.PropertyId ?? Guid.Empty,
            leaseId: request.LeaseId,
            asOfMonth: request.AsOfMonth,
            toMonth: request.ToMonth,
            ct: ct);

        var warnings = new List<ReceivablesApplyWarningDto>();

        if (open.Charges.All(x => x.OutstandingAmount <= 0m))
            warnings.Add(new ReceivablesApplyWarningDto(WarnNoCharges, "No open charges were found for the requested range."));

        if (open.Credits.All(x => x.AvailableCredit <= 0m))
            warnings.Add(new ReceivablesApplyWarningDto(WarnNoCredits, "No available payment credits were found for the requested range."));

        var plan = ReceivablesFifoAllocator.Allocate(
            charges: open.Charges,
            credits: open.Credits,
            limit: request.Limit);

        var creditTypesById = open.Credits
            .GroupBy(x => x.CreditDocumentId)
            .ToDictionary(g => g.Key, g => g.First().DocumentType);

        var suggested = plan.Lines
            .Select(x => new ReceivablesSuggestedLeaseApplyDto(
                ApplyId: null,
                CreditDocumentId: x.CreditDocumentId,
                CreditDocumentType: creditTypesById.TryGetValue(x.CreditDocumentId, out var creditDocumentType)
                    ? creditDocumentType
                    : PropertyManagementCodes.ReceivablePayment,
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
                ApplyPayload: ReceivablesApplyExecutionHelpers.BuildApplyPayload(
                    creditDocumentId: x.CreditDocumentId,
                    chargeDocumentId: x.ChargeDocumentId,
                    appliedOnUtc: x.CreditDocumentDateUtc,
                    amount: x.Amount)))
            .ToList();

        var totalApplied = suggested.Sum(x => x.Amount);
        var remainingOutstanding = plan.RemainingOutstanding;
        var remainingCredit = plan.RemainingCredit;

        if (plan.LimitReached)
            warnings.Add(new ReceivablesApplyWarningDto(WarnLimitReached, "The suggestion limit was reached. Some items may remain unapplied."));

        if (remainingOutstanding > 0m)
            warnings.Add(new ReceivablesApplyWarningDto(WarnOutstandingRemaining, $"Outstanding charges remain: {remainingOutstanding:0.##}."));

        if (remainingCredit > 0m)
            warnings.Add(new ReceivablesApplyWarningDto(WarnCreditRemaining, $"Unapplied credits remain: {remainingCredit:0.##}."));

        if (request.CreateDrafts && suggested.Count > 0)
        {
            var withIds = await CreateDraftAppliesAsync(suggested, ct);
            suggested = withIds.ToList();
        }

        return new ReceivablesSuggestFifoApplyResponse(
            RegisterId: open.RegisterId,
            PartyId: open.PartyId,
            PartyDisplay: open.PartyDisplay,
            PropertyId: open.PropertyId,
            PropertyDisplay: open.PropertyDisplay,
            LeaseId: open.LeaseId,
            LeaseDisplay: open.LeaseDisplay,
            TotalOutstanding: open.TotalOutstanding,
            TotalCredit: open.TotalCredit,
            TotalApplied: totalApplied,
            RemainingOutstanding: remainingOutstanding,
            RemainingCredit: remainingCredit,
            SuggestedApplies: suggested,
            Warnings: warnings);
    }

    private async Task<IReadOnlyList<ReceivablesSuggestedLeaseApplyDto>> CreateDraftAppliesAsync(
        IReadOnlyList<ReceivablesSuggestedLeaseApplyDto> plan,
        CancellationToken ct)
    {
        var result = new List<ReceivablesSuggestedLeaseApplyDto>(plan.Count);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            foreach (var s in plan)
            {
                var dateUtc = DateTime.SpecifyKind(
                    s.CreditDocumentDateUtc.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Utc);

                var applyId = await ReceivablesApplyExecutionHelpers.CreateApplyDraftAndUpsertHeadAsync(
                    drafts: drafts,
                    relationships: relationships,
                    headWriter: applyHeadWriter,
                    typeCode: PropertyManagementCodes.ReceivableApply,
                    dateUtc: dateUtc,
                    creditDocumentId: s.CreditDocumentId,
                    chargeDocumentId: s.ChargeDocumentId,
                    appliedOnUtc: s.CreditDocumentDateUtc,
                    amount: s.Amount,
                    memo: null,
                    ct: innerCt);

                result.Add(s with { ApplyId = applyId });
            }
        }, ct);

        return result;
    }
}
