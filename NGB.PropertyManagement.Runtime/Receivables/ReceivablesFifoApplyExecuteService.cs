using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// Executes a FIFO allocation plan by creating and posting a batch of pm.receivable_apply documents.
///
/// Notes:
/// - Uses a single DB transaction to avoid partial application when a later apply fails.
/// - The plan is computed first (no writes) and then executed.
/// - Posting-time validation guards over-apply and insufficient credit.
/// </summary>
public sealed class ReceivablesFifoApplyExecuteService(
    IReceivablesFifoApplySuggestService suggest,
    IDocumentDraftService drafts,
    IDocumentPostingService posting,
    IDocumentRelationshipService relationships,
    IReceivableApplyHeadWriter applyHeadWriter,
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IAdvisoryLockManager advisoryLocks,
    IUnitOfWork uow)
    : IReceivablesFifoApplyExecuteService
{
    public async Task<ReceivablesFifoApplyExecuteResponse> ExecuteAsync(
        ReceivablesFifoApplyExecuteRequest request,
        CancellationToken ct = default)
    {
        if (request.CreditDocumentId == Guid.Empty)
            throw ReceivablesRequestValidationException.PaymentRequired();

        if (request.MaxApplications is not null && request.MaxApplications <= 0)
            throw ReceivablesRequestValidationException.MaxApplicationsInvalid();

        // 1) Plan (no writes).
        var plan = await suggest.SuggestAsync(
            new ReceivablesFifoApplySuggestRequest(request.CreditDocumentId, request.MaxApplications),
            ct);

        if (plan.SuggestedApplies.Count == 0)
        {
            return new ReceivablesFifoApplyExecuteResponse(
                CreditDocumentId: request.CreditDocumentId,
                RegisterId: plan.RegisterId,
                TotalApplied: 0m,
                RemainingCredit: plan.AvailableCredit,
                ExecutedApplies: []);
        }

        // 2) Execute atomically.
        var executed = new List<ReceivablesExecutedApplyDto>(plan.SuggestedApplies.Count);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // Lock all involved documents deterministically to avoid deadlocks with other apply flows.
            var ids = new List<Guid>(1 + plan.SuggestedApplies.Count) { request.CreditDocumentId };
            ids.AddRange(plan.SuggestedApplies.Select(x => x.ChargeDocumentId));
            await ReceivablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(advisoryLocks, ids, innerCt);

            var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, documents, request.CreditDocumentId, innerCt);
            var dateUtc = DateTime.SpecifyKind(creditSource.CreditDateUtc.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

            foreach (var s in plan.SuggestedApplies)
            {
                if (s.Amount <= 0m)
                    continue;

                var applyId = await ReceivablesApplyExecutionHelpers.CreateApplyDraftAndUpsertHeadAsync(
                    drafts: drafts,
                    relationships: relationships,
                    headWriter: applyHeadWriter,
                    typeCode: PropertyManagementCodes.ReceivableApply,
                    dateUtc: dateUtc,
                    creditDocumentId: request.CreditDocumentId,
                    chargeDocumentId: s.ChargeDocumentId,
                    appliedOnUtc: creditSource.CreditDateUtc,
                    amount: s.Amount,
                    memo: null,
                    ct: innerCt);

                // Post inside the same outer transaction.
                await posting.PostAsync(applyId, manageTransaction: false, ct: innerCt);

                executed.Add(new ReceivablesExecutedApplyDto(applyId, s.ChargeDocumentId, s.Amount));
            }
        }, ct);

        var totalApplied = executed.Sum(x => x.Amount);
        var remaining = Math.Max(0m, plan.AvailableCredit - totalApplied);

        return new ReceivablesFifoApplyExecuteResponse(
            CreditDocumentId: request.CreditDocumentId,
            RegisterId: plan.RegisterId,
            TotalApplied: totalApplied,
            RemainingCredit: remaining,
            ExecutedApplies: executed);
    }

}
