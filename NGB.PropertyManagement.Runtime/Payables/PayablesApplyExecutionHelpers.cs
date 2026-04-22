using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Locks;
using NGB.PropertyManagement.Payables;
using NGB.Runtime.Documents;
using NGB.Tools;

namespace NGB.PropertyManagement.Runtime.Payables;

internal static class PayablesApplyExecutionHelpers
{
    private const string BasedOnRelationshipCode = "based_on";

    public static async Task LockDocumentsDeterministicallyAsync(
        IAdvisoryLockManager locks,
        IEnumerable<Guid> documentIds,
        CancellationToken ct)
    {
        foreach (var id in documentIds.Where(x => x != Guid.Empty).Distinct().OrderBy(x => x))
            await locks.LockDocumentAsync(id, ct);
    }

    public static RecordPayload BuildApplyPayload(
        Guid creditDocumentId,
        Guid chargeDocumentId,
        DateOnly appliedOnUtc,
        decimal amount,
        string? memo = null)
    {
        var fields = new Dictionary<string, JsonElement>
        {
            ["credit_document_id"] = JsonTools.J(creditDocumentId),
            ["charge_document_id"] = JsonTools.J(chargeDocumentId),
            ["applied_on_utc"] = JsonTools.J(appliedOnUtc.ToString("yyyy-MM-dd")),
            ["amount"] = JsonTools.J(amount)
        };

        if (!string.IsNullOrWhiteSpace(memo))
            fields["memo"] = JsonTools.J(memo);

        return new RecordPayload(fields);
    }

    public static async Task EnsureApplyRelationshipsAsync(
        IDocumentRelationshipService relationships,
        Guid applyId,
        Guid creditDocumentId,
        Guid chargeDocumentId,
        CancellationToken ct)
    {
        // Keep payable apply relationships explicit for the same reason as receivable apply:
        // one apply document materializes two business-flow edges and therefore does not fit
        // the single-field mirrored-relationship model.
        await relationships.CreateAsync(applyId, creditDocumentId, BasedOnRelationshipCode, manageTransaction: false, ct: ct);
        await relationships.CreateAsync(applyId, chargeDocumentId, BasedOnRelationshipCode, manageTransaction: false, ct: ct);
    }

    public static async Task<Guid> CreateApplyDraftAndUpsertHeadAsync(
        IDocumentDraftService drafts,
        IDocumentRelationshipService relationships,
        IPayableApplyHeadWriter headWriter,
        DateTime dateUtc,
        Guid creditDocumentId,
        Guid chargeDocumentId,
        DateOnly appliedOnUtc,
        decimal amount,
        string? memo,
        CancellationToken ct)
    {
        var applyId = await drafts.CreateDraftAsync(
            PropertyManagementCodes.PayableApply,
            number: null,
            dateUtc,
            manageTransaction: false,
            ct: ct);

        await headWriter.UpsertAsync(applyId, creditDocumentId, chargeDocumentId, appliedOnUtc, amount, memo, ct);
        await EnsureApplyRelationshipsAsync(relationships, applyId, creditDocumentId, chargeDocumentId, ct);

        return applyId;
    }
}
