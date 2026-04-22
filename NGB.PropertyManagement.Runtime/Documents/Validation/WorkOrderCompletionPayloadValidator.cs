using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal sealed class WorkOrderCompletionPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.WorkOrderCompletion;

    public async Task ValidateCreateDraftPayloadAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        var snapshot = await ResolveSnapshotAsync(documentId: null, payload, ct);
        if (snapshot is null)
            return;

        await ValidateBusinessRulesAsync(snapshot.Value, ct);
    }

    public async Task ValidateUpdateDraftPayloadAsync(
        Guid documentId,
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        if (payload.Fields is null || payload.Fields.Count == 0)
            return;

        var snapshot = await ResolveSnapshotAsync(documentId, payload, ct);
        if (snapshot is null)
            return;

        await ValidateBusinessRulesAsync(snapshot.Value, ct);
    }

    private async Task<Snapshot?> ResolveSnapshotAsync(Guid? documentId, RecordPayload payload, CancellationToken ct)
    {
        var current = documentId is null
            ? null
            : await readers.ReadWorkOrderCompletionHeadAsync(documentId.Value, ct);

        var fields = payload.Fields;

        _ = TryGetGuid(fields, "work_order_id", out var workOrderId);
        _ = TryGetString(fields, "outcome", out var outcome);

        workOrderId ??= current?.WorkOrderId;
        outcome ??= current?.Outcome;

        if (workOrderId is null || outcome is null)
            return null;

        return new Snapshot(documentId, workOrderId.Value, outcome);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        WorkOrderCompletionValidationGuards.NormalizeOutcomeOrThrow(snapshot.Outcome, snapshot.DocumentId);

        await WorkOrderCompletionValidationGuards.ValidateWorkOrderAsync(
            snapshot.WorkOrderId,
            snapshot.DocumentId,
            documents,
            ct);

        await WorkOrderCompletionValidationGuards.EnsureNoOtherPostedCompletionAsync(
            snapshot.WorkOrderId,
            snapshot.DocumentId,
            snapshot.DocumentId,
            readers,
            ct);
    }

    private static bool TryGetGuid(IReadOnlyDictionary<string, JsonElement>? fields, string key, out Guid? value)
    {
        value = null;
        if (fields is null || !fields.TryGetValue(key, out var el))
            return false;

        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        try
        {
            value = el.ParseGuidOrRef();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetString(IReadOnlyDictionary<string, JsonElement>? fields, string key, out string? value)
    {
        value = null;
        if (fields is null || !fields.TryGetValue(key, out var el))
            return false;

        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        try
        {
            value = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct Snapshot(Guid? DocumentId, Guid WorkOrderId, string Outcome);
}
