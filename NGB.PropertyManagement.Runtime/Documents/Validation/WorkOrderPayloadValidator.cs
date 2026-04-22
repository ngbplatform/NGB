using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal sealed class WorkOrderPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    ICatalogRepository catalogRepository)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.WorkOrder;

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
            : await readers.ReadWorkOrderHeadAsync(documentId.Value, ct);

        var fields = payload.Fields;

        _ = TryGetGuid(fields, "request_id", out var requestId);
        var hasAssignedPartyId = TryGetGuid(fields, "assigned_party_id", out var assignedPartyId);
        _ = TryGetString(fields, "cost_responsibility", out var costResponsibility);

        requestId ??= current?.RequestId;
        if (!hasAssignedPartyId)
            assignedPartyId ??= current?.AssignedPartyId;

        costResponsibility ??= current?.CostResponsibility;

        if (requestId is null || costResponsibility is null)
            return null;

        return new Snapshot(documentId, requestId.Value, assignedPartyId, costResponsibility);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        WorkOrderValidationGuards.NormalizeCostResponsibilityOrThrow(snapshot.CostResponsibility, snapshot.DocumentId);
        await WorkOrderValidationGuards.ValidateRequestAsync(snapshot.RequestId, snapshot.DocumentId, documents, ct);

        if (snapshot.AssignedPartyId is not null)
            await WorkOrderValidationGuards.ValidateAssignedPartyAsync(snapshot.AssignedPartyId.Value, snapshot.DocumentId, catalogRepository, ct);
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

    private readonly record struct Snapshot(
        Guid? DocumentId,
        Guid RequestId,
        Guid? AssignedPartyId,
        string CostResponsibility);
}
