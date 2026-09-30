using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.WorkCenter;
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
    IUnitOfWork uow,
    IReceivablePaymentWorkCenterSynchronizer workCenter,
    IDocumentPostingReadCache? postingReadCache = null)
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

        if (request.MaxApplications > FifoApplyLimits.MaxAtomicApplications)
            throw ReceivablesRequestValidationException.MaxApplicationsTooLarge(FifoApplyLimits.MaxAtomicApplications);

        var maxApplications = request.MaxApplications ?? FifoApplyLimits.DefaultMaxAtomicApplications;

        using var postingReadScope = postingReadCache?.BeginScope();

        // 1) Plan (no writes).
        var plan = await suggest.SuggestAsync(
            new ReceivablesFifoApplySuggestRequest(request.CreditDocumentId, maxApplications),
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
        var changedUsers = await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // Lock all involved documents deterministically to avoid deadlocks with other apply flows.
            var ids = new List<Guid>(1 + plan.SuggestedApplies.Count) { request.CreditDocumentId };
            ids.AddRange(plan.SuggestedApplies.Select(x => x.ChargeDocumentId));
            await ReceivablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(advisoryLocks, ids, innerCt);

            var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, documents, request.CreditDocumentId, innerCt);
            var dateUtc = DateTime.SpecifyKind(creditSource.CreditDateUtc.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

            var suggestions = plan.SuggestedApplies
                .Where(static suggestion => suggestion.Amount > 0m)
                .ToArray();
            var applyIds = await ReceivablesApplyExecutionHelpers.CreateApplyDraftsAndUpsertHeadsAsync(
                drafts,
                relationships,
                applyHeadWriter,
                suggestions.Select(suggestion => new ReceivablesApplyExecutionHelpers.ApplyDraftRequest(
                        PropertyManagementCodes.ReceivableApply,
                        dateUtc,
                        request.CreditDocumentId,
                        suggestion.ChargeDocumentId,
                        creditSource.CreditDateUtc,
                        suggestion.Amount,
                        Memo: null))
                    .ToArray(),
                innerCt);

            if (posting is IDocumentPostingBatchService batchPosting)
            {
                await batchPosting.PostManyAsync(applyIds, manageTransaction: false, ct: innerCt);
            }
            else
            {
                foreach (var applyId in applyIds)
                {
                    await posting.PostAsync(applyId, manageTransaction: false, ct: innerCt);
                }
            }

            for (var index = 0; index < suggestions.Length; index++)
            {
                var suggestion = suggestions[index];
                var applyId = applyIds[index];

                executed.Add(new ReceivablesExecutedApplyDto(applyId, suggestion.ChargeDocumentId, suggestion.Amount));
            }

            if (executed.Count > 0)
                return (IReadOnlyCollection<Guid>)(await workCenter.CompleteIfExhaustedAsync(request.CreditDocumentId, innerCt));

            return Array.Empty<Guid>();
        }, ct);

        if (executed.Count > 0)
            await workCenter.NotifyChangedAsync(changedUsers, ct);

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
