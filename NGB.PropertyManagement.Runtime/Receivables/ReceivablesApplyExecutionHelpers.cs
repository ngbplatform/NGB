using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Locks;
using NGB.Runtime.Documents;
using NGB.PropertyManagement.Receivables;
using NGB.Tools;

namespace NGB.PropertyManagement.Runtime.Receivables;

internal static class ReceivablesApplyExecutionHelpers
{
    private const string BasedOnRelationshipCode = "based_on";

    public static async Task LockDocumentsDeterministicallyAsync(
        IAdvisoryLockManager locks,
        IEnumerable<Guid> documentIds,
        CancellationToken ct)
    {
        var ordered = documentIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        foreach (var id in ordered)
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

    public static async Task<Guid> CreateApplyDraftAndUpsertHeadAsync(
        IDocumentDraftService drafts,
        IDocumentRelationshipService relationships,
        IReceivableApplyHeadWriter headWriter,
        string typeCode,
        DateTime dateUtc,
        Guid creditDocumentId,
        Guid chargeDocumentId,
        DateOnly appliedOnUtc,
        decimal amount,
        string? memo,
        CancellationToken ct)
    {
        var applyId = await drafts.CreateDraftAsync(
            typeCode: typeCode,
            number: null,
            dateUtc: dateUtc,
            manageTransaction: false,
            ct: ct);

        await headWriter.UpsertAsync(
            documentId: applyId,
            creditDocumentId: creditDocumentId,
            chargeDocumentId: chargeDocumentId,
            appliedOnUtc: appliedOnUtc,
            amount: amount,
            memo: memo,
            ct: ct);

        // Relationships are draft-only mutations. Write them immediately after draft creation.
        await EnsureApplyRelationshipsAsync(relationships, applyId, creditDocumentId, chargeDocumentId, ct);

        return applyId;
    }

    public static async Task EnsureApplyRelationshipsAsync(
        IDocumentRelationshipService relationships,
        Guid applyId,
        Guid creditDocumentId,
        Guid chargeDocumentId,
        CancellationToken ct)
    {
        // Two directed edges (apply -> credit source, apply -> charge) are enough.
        // Graph traversal is BOTH directions, so UI can explain balances from any node.
        // This stays as explicit persisted relationship logic instead of mirrored-field metadata,
        // because apply flow is inherently multi-edge and not a simple single-field provenance mapping.
        await relationships.CreateAsync(
            fromDocumentId: applyId,
            toDocumentId: creditDocumentId,
            relationshipCode: BasedOnRelationshipCode,
            manageTransaction: false,
            ct: ct);

        await relationships.CreateAsync(
            fromDocumentId: applyId,
            toDocumentId: chargeDocumentId,
            relationshipCode: BasedOnRelationshipCode,
            manageTransaction: false,
            ct: ct);
    }
}
