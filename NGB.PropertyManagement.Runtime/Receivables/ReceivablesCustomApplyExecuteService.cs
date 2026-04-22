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
/// Executes a custom allocation plan by atomically creating and posting a batch of pm.receivable_apply documents.
///
/// Intended for UI "manual apply" flows (single request, no partial writes).
/// </summary>
public sealed class ReceivablesCustomApplyExecuteService(
    IReceivablesOpenItemsService openItems,
    IDocumentDraftService drafts,
    IDocumentPostingService posting,
    IDocumentRelationshipService relationships,
    IReceivableApplyHeadWriter applyHeadWriter,
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IAdvisoryLockManager advisoryLocks,
    IUnitOfWork uow)
    : IReceivablesCustomApplyExecuteService
{
    private const int MaxLines = 500;

    public async Task<ReceivablesCustomApplyExecuteResponse> ExecuteAsync(
        ReceivablesCustomApplyExecuteRequest request,
        CancellationToken ct = default)
    {
        if (request.CreditDocumentId == Guid.Empty)
            throw ReceivablesRequestValidationException.PaymentRequired();

        if (request.Applies is null || request.Applies.Count == 0)
            throw ReceivablesRequestValidationException.ApplicationsRequired();

        if (request.Applies.Count > MaxLines)
            throw ReceivablesRequestValidationException.ApplicationsTooLarge(request.Applies.Count, MaxLines);

        // Canonicalize lines before going to the DB.
        var grouped = new Dictionary<Guid, decimal>();
        for (var i = 0; i < request.Applies.Count; i++)
        {
            var line = request.Applies[i];
            if (line is null)
                continue;

            if (line.ChargeDocumentId == Guid.Empty)
                throw ReceivablesRequestValidationException.ChargeRequired(i);

            if (line.ChargeDocumentId == request.CreditDocumentId)
                throw ReceivableApplyValidationException.PaymentAndChargeMustMatch(request.CreditDocumentId, line.ChargeDocumentId);

            if (line.Amount <= 0m)
                throw ReceivableApplyValidationException.AmountMustBePositive(line.Amount);

            grouped.TryGetValue(line.ChargeDocumentId, out var existing);
            grouped[line.ChargeDocumentId] = existing + line.Amount;
        }

        var allocations = grouped
            .Where(x => x.Value > 0m)
            .OrderBy(x => x.Key)
            .Select(x => new ReceivablesCustomApplyLine(x.Key, x.Value))
            .ToArray();

        if (allocations.Length == 0)
            throw ReceivablesRequestValidationException.PositiveApplicationAmountRequired();

        var executed = new List<ReceivablesExecutedApplyDto>(allocations.Length);
        var registerId = Guid.Empty;
        var availableCredit = 0m;

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // Lock all involved documents in a deterministic order to avoid deadlocks.
            var docIds = new List<Guid>(1 + allocations.Length) { request.CreditDocumentId };
            docIds.AddRange(allocations.Select(x => x.ChargeDocumentId));
            await ReceivablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(advisoryLocks, docIds, innerCt);

            var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, documents, request.CreditDocumentId, innerCt);
            var dateUtc = DateTime.SpecifyKind(creditSource.CreditDateUtc.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

            // Read current open-items state inside the same transaction (no nested transactions).
            var open = await openItems.GetOpenItemsAsync(creditSource.PartyId, creditSource.PropertyId, creditSource.LeaseId, innerCt);
            registerId = open.RegisterId;

            var credit = open.Credits.FirstOrDefault(x => x.ItemId == request.CreditDocumentId);
            availableCredit = credit?.Amount ?? 0m;

            var totalRequested = allocations.Sum(x => x.Amount);

            if (availableCredit <= 0m)
                throw ReceivableApplyValidationException.InsufficientCredit(request.CreditDocumentId, totalRequested, availableCredit);

            if (availableCredit < totalRequested)
                throw ReceivableApplyValidationException.InsufficientCredit(request.CreditDocumentId, totalRequested, availableCredit);

            var outstandingByCharge = open.Charges.ToDictionary(x => x.ItemId, x => x.Amount);

            foreach (var a in allocations)
            {
                outstandingByCharge.TryGetValue(a.ChargeDocumentId, out var outstanding);

                if (outstanding < a.Amount)
                    throw ReceivableApplyValidationException.OverApplyCharge(a.ChargeDocumentId, a.Amount, outstanding);
            }

            foreach (var a in allocations)
            {
                var applyId = await drafts.CreateDraftAsync(
                    typeCode: PropertyManagementCodes.ReceivableApply,
                    number: null,
                    dateUtc: dateUtc,
                    manageTransaction: false,
                    ct: innerCt);

                await applyHeadWriter.UpsertAsync(
                    documentId: applyId,
                    creditDocumentId: request.CreditDocumentId,
                    chargeDocumentId: a.ChargeDocumentId,
                    appliedOnUtc: creditSource.CreditDateUtc,
                    amount: a.Amount,
                    memo: null,
                    ct: innerCt);

                await ReceivablesApplyExecutionHelpers.EnsureApplyRelationshipsAsync(
                    relationships,
                    applyId,
                    creditDocumentId: request.CreditDocumentId,
                    chargeDocumentId: a.ChargeDocumentId,
                    ct: innerCt);

                await posting.PostAsync(applyId, manageTransaction: false, ct: innerCt);

                executed.Add(new ReceivablesExecutedApplyDto(applyId, a.ChargeDocumentId, a.Amount));
            }
        }, ct);

        var totalApplied = executed.Sum(x => x.Amount);
        var remaining = Math.Max(0m, availableCredit - totalApplied);

        return new ReceivablesCustomApplyExecuteResponse(
            CreditDocumentId: request.CreditDocumentId,
            RegisterId: registerId,
            TotalApplied: totalApplied,
            RemainingCredit: remaining,
            ExecutedApplies: executed);
    }
}
