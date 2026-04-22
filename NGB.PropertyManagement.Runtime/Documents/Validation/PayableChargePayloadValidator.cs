using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;
using NGB.Application.Abstractions.Services;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal sealed class PayableChargePayloadValidator(
    IPropertyManagementDocumentReaders readers,
    ICatalogRepository catalogRepository,
    ICatalogService catalogService)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.PayableCharge;

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
            : await readers.ReadPayableChargeHeadAsync(documentId.Value, ct);

        var fields = payload.Fields;

        TryGetGuid(fields, "party_id", out var partyId);
        TryGetGuid(fields, "property_id", out var propertyId);
        TryGetGuid(fields, "charge_type_id", out var chargeTypeId);
        TryGetDecimal(fields, "amount", out var amount);

        partyId ??= current?.PartyId;
        propertyId ??= current?.PropertyId;
        chargeTypeId ??= current?.ChargeTypeId;
        amount ??= current?.Amount;

        if (partyId is null || propertyId is null || chargeTypeId is null || amount is null)
            return null;

        return new Snapshot(documentId, partyId.Value, propertyId.Value, chargeTypeId.Value, amount.Value);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Amount <= 0m)
            throw PayableChargeValidationException.AmountMustBePositive(snapshot.Amount, snapshot.DocumentId);

        await PayableChargeValidationGuards.ValidateVendorAsync(snapshot.PartyId, snapshot.DocumentId, catalogRepository, catalogService, ct);
        await PayableChargeValidationGuards.ValidatePropertyAsync(snapshot.PropertyId, snapshot.DocumentId, readers, ct);
        await PayableChargeValidationGuards.ValidateChargeTypeAsync(snapshot.ChargeTypeId, snapshot.DocumentId, catalogRepository, ct);
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

    private static bool TryGetDecimal(IReadOnlyDictionary<string, JsonElement>? fields, string key, out decimal? value)
    {
        value = null;
        if (fields is null || !fields.TryGetValue(key, out var el))
            return false;

        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        try
        {
            value = el.ValueKind == JsonValueKind.Number
                ? el.GetDecimal()
                : decimal.Parse(el.GetString() ?? el.ToString(), CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct Snapshot(
        Guid? DocumentId,
        Guid PartyId,
        Guid PropertyId,
        Guid ChargeTypeId,
        decimal Amount);
}
