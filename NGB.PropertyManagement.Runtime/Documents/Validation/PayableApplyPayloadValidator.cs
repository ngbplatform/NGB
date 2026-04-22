using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal sealed class PayableApplyPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.PayableApply;

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
            : await readers.ReadPayableApplyHeadAsync(documentId.Value, ct);

        var fields = payload.Fields;

        TryGetGuid(fields, "credit_document_id", out var creditDocumentId);
        TryGetGuid(fields, "charge_document_id", out var chargeDocumentId);
        TryGetDate(fields, "applied_on_utc", out var appliedOnUtc);
        TryGetDecimal(fields, "amount", out var amount);

        creditDocumentId ??= current?.CreditDocumentId;
        chargeDocumentId ??= current?.ChargeDocumentId;
        appliedOnUtc ??= current?.AppliedOnUtc;
        amount ??= current?.Amount;

        if (creditDocumentId is null || chargeDocumentId is null || appliedOnUtc is null || amount is null)
            return null;

        return new Snapshot(creditDocumentId.Value, chargeDocumentId.Value, appliedOnUtc.Value, amount.Value);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Amount <= 0m)
            throw PayableApplyValidationException.AmountMustBePositive(snapshot.Amount);

        if (snapshot.CreditDocumentId == snapshot.ChargeDocumentId)
            throw PayableApplyValidationException.CreditSourceAndChargeMustDiffer(snapshot.CreditDocumentId, snapshot.ChargeDocumentId);

        var creditDocument = await documents.GetAsync(snapshot.CreditDocumentId, ct);
        if (creditDocument is null)
            throw PayableApplyValidationException.CreditSourceNotFound(snapshot.CreditDocumentId);

        if (!PayableCreditSourceResolver.IsCreditSourceDocumentType(creditDocument.TypeCode))
            throw PayableApplyValidationException.CreditSourceWrongType(snapshot.CreditDocumentId, creditDocument.TypeCode);

        var chargeDocument = await documents.GetAsync(snapshot.ChargeDocumentId, ct);
        if (chargeDocument is null)
            throw PayableApplyValidationException.ChargeNotFound(snapshot.ChargeDocumentId);

        if (!string.Equals(chargeDocument.TypeCode, PropertyManagementCodes.PayableCharge, StringComparison.OrdinalIgnoreCase))
            throw PayableApplyValidationException.ChargeWrongType(snapshot.ChargeDocumentId, chargeDocument.TypeCode);

        var creditSource = await PayableCreditSourceResolver.ReadRequiredAsync(readers, creditDocument, ct);
        var charge = await readers.ReadPayableChargeHeadAsync(snapshot.ChargeDocumentId, ct);

        if (creditSource.PartyId != charge.PartyId || creditSource.PropertyId != charge.PropertyId)
            throw PayableApplyValidationException.PartyPropertyMismatch(snapshot.CreditDocumentId, snapshot.ChargeDocumentId);
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

    private static bool TryGetDate(IReadOnlyDictionary<string, JsonElement>? fields, string key, out DateOnly? value)
    {
        value = null;
        if (fields is null || !fields.TryGetValue(key, out var el))
            return false;

        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        try
        {
            value = DateOnly.Parse(el.GetString() ?? el.ToString(), CultureInfo.InvariantCulture);
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
        Guid CreditDocumentId,
        Guid ChargeDocumentId,
        DateOnly AppliedOnUtc,
        decimal Amount);
}
