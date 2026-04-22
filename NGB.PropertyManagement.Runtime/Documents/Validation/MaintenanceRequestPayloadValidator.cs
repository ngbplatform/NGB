using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal sealed class MaintenanceRequestPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    ICatalogRepository catalogRepository,
    IPropertyManagementPartyReader parties)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.MaintenanceRequest;

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
            : await readers.ReadMaintenanceRequestHeadAsync(documentId.Value, ct);

        var fields = payload.Fields;

        TryGetGuid(fields, "property_id", out var propertyId);
        TryGetGuid(fields, "party_id", out var partyId);
        TryGetGuid(fields, "category_id", out var categoryId);
        TryGetString(fields, "priority", out var priority);
        TryGetString(fields, "subject", out var subject);

        propertyId ??= current?.PropertyId;
        partyId ??= current?.PartyId;
        categoryId ??= current?.CategoryId;
        priority ??= current?.Priority;
        subject ??= current?.Subject;

        if (propertyId is null || partyId is null || categoryId is null || priority is null || subject is null)
            return null;

        return new Snapshot(documentId, propertyId.Value, partyId.Value, categoryId.Value, priority, subject);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Subject))
            throw MaintenanceRequestValidationException.SubjectRequired(snapshot.DocumentId);

        MaintenanceRequestValidationGuards.NormalizePriorityOrThrow(snapshot.Priority, snapshot.DocumentId);
        await MaintenanceRequestValidationGuards.ValidatePropertyAsync(snapshot.PropertyId, snapshot.DocumentId, readers, ct);
        await MaintenanceRequestValidationGuards.ValidatePartyAsync(snapshot.PartyId, snapshot.DocumentId, parties, ct);
        await MaintenanceRequestValidationGuards.ValidateCategoryAsync(snapshot.CategoryId, snapshot.DocumentId, catalogRepository, ct);
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
        Guid PropertyId,
        Guid PartyId,
        Guid CategoryId,
        string Priority,
        string Subject);
}
