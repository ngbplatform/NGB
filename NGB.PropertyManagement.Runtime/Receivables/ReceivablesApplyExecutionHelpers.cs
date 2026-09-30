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

        if (locks is IAdvisoryLockBatchManager batchLocks)
        {
            await batchLocks.LockDocumentsAsync(ordered, ct);
            return;
        }

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
        var ids = await CreateApplyDraftsAndUpsertHeadsAsync(
            drafts,
            relationships,
            headWriter,
            [new ApplyDraftRequest(typeCode, dateUtc, creditDocumentId, chargeDocumentId, appliedOnUtc, amount, memo)],
            ct);

        return ids[0];
    }

    public static async Task<IReadOnlyList<Guid>> CreateApplyDraftsAndUpsertHeadsAsync(
        IDocumentDraftService drafts,
        IDocumentRelationshipService? relationships,
        IReceivableApplyHeadWriter headWriter,
        IReadOnlyList<ApplyDraftRequest> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0)
            return [];

        IReadOnlyList<Guid> applyIds;
        if (drafts is IDocumentDraftBatchService batchDrafts)
        {
            applyIds = await batchDrafts.CreateDraftsAsync(
                requests.Select(static request => new DocumentDraftCreateRequest(
                        request.TypeCode,
                        Number: null,
                        request.DateUtc))
                    .ToArray(),
                manageTransaction: false,
                ct: ct);
        }
        else
        {
            var created = new Guid[requests.Count];
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                created[index] = await drafts.CreateDraftAsync(
                    request.TypeCode,
                    number: null,
                    request.DateUtc,
                    manageTransaction: false,
                    ct: ct);
            }

            applyIds = created;
        }

        var headWrites = requests.Select((request, index) => new ReceivableApplyHeadWrite(
                applyIds[index],
                request.CreditDocumentId,
                request.ChargeDocumentId,
                request.AppliedOnUtc,
                request.Amount,
                request.Memo))
            .ToArray();

        if (headWriter is IReceivableApplyHeadBatchWriter batchHeadWriter)
        {
            await batchHeadWriter.UpsertManyAsync(headWrites, ct);
        }
        else
        {
            foreach (var head in headWrites)
            {
                await headWriter.UpsertAsync(
                    head.DocumentId,
                    head.CreditDocumentId,
                    head.ChargeDocumentId,
                    head.AppliedOnUtc,
                    head.Amount,
                    head.Memo,
                    ct);
            }
        }

        if (relationships is IDocumentRelationshipBatchService batchRelationships)
        {
            await batchRelationships.CreateManyAsync(
                requests.SelectMany((request, index) => new[]
                    {
                        new DocumentRelationshipCreateRequest(applyIds[index], request.CreditDocumentId, BasedOnRelationshipCode),
                        new DocumentRelationshipCreateRequest(applyIds[index], request.ChargeDocumentId, BasedOnRelationshipCode)
                    })
                    .ToArray(),
                manageTransaction: false,
                ct: ct);
        }
        else if (relationships is not null)
        {
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                await EnsureApplyRelationshipsAsync(
                    relationships,
                    applyIds[index],
                    request.CreditDocumentId,
                    request.ChargeDocumentId,
                    ct);
            }
        }

        return applyIds;
    }

    public sealed record ApplyDraftRequest(
        string TypeCode,
        DateTime DateUtc,
        Guid CreditDocumentId,
        Guid ChargeDocumentId,
        DateOnly AppliedOnUtc,
        decimal Amount,
        string? Memo);

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
