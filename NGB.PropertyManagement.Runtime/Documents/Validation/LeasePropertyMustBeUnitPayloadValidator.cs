using System.Text.Json;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents.Validation;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

/// <summary>
/// Draft-time invariant for pm.lease:
/// - property_id must reference an existing, non-deleted pm.property with kind = Unit.
///
/// Posting-time validation already enforces this invariant (see <see cref="LeaseOverlapPostValidator"/>),
/// but drafts should be kept consistent too (and we want a friendly error on Save, not only on Post).
/// </summary>
internal sealed class LeasePropertyMustBeUnitPayloadValidator(IPropertyManagementDocumentReaders readers)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.Lease;

    public async Task ValidateCreateDraftPayloadAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        await ValidateAsync(payload, requirePropertyId: true, ct);
    }

    public async Task ValidateUpdateDraftPayloadAsync(
        Guid documentId,
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        if (payload.Fields is null || payload.Fields.Count == 0)
            return;

        if (!payload.Fields.ContainsKey("property_id"))
            return;

        await ValidateAsync(payload, requirePropertyId: false, ct);
    }

    private async Task ValidateAsync(RecordPayload payload, bool requirePropertyId, CancellationToken ct)
    {
        if (payload.Fields is null || !payload.Fields.TryGetValue("property_id", out var el))
        {
            if (requirePropertyId)
                throw DocumentPropertyPayloadValidationException.Required(TypeCode);

            return;
        }

        var propertyId = ExtractGuid(el);
        if (propertyId == Guid.Empty)
            throw DocumentPropertyPayloadValidationException.Required(TypeCode);

        var property = await readers.ReadPropertyHeadAsync(propertyId, ct);
        if (property is null)
            throw new LeasePropertyNotFoundException(propertyId);

        if (property.IsDeleted)
            throw new LeasePropertyDeletedException(propertyId);

        if (!string.Equals(property.Kind, "Unit", StringComparison.OrdinalIgnoreCase))
            throw new LeasePropertyMustBeUnitException(propertyId, property.Kind);
    }

    private Guid ExtractGuid(JsonElement el)
    {
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Guid.Empty;

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return Guid.Empty;

            if (Guid.TryParse(s, out var g))
                return g;

            throw DocumentPropertyPayloadValidationException.Invalid(TypeCode);
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            if (!el.TryGetProperty("id", out var idEl) && !el.TryGetProperty("Id", out idEl))
                throw DocumentPropertyPayloadValidationException.Invalid(TypeCode);

            if (idEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return Guid.Empty;

            var s = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.ToString();
            if (string.IsNullOrWhiteSpace(s))
                return Guid.Empty;

            if (Guid.TryParse(s, out var g))
                return g;

            throw DocumentPropertyPayloadValidationException.Invalid(TypeCode);
        }

        throw DocumentPropertyPayloadValidationException.Invalid(TypeCode);
    }
}
