using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

/// <summary>
/// Draft-time domain validation for pm.receivable_apply.
///
/// Business rules enforced here:
/// - amount must be positive;
/// - credit_document_id and charge_document_id must be different;
/// - credit_document_id must reference an existing receivable credit source
///   (pm.receivable_payment or pm.receivable_credit_memo);
/// - charge_document_id must reference an existing charge-like receivable document;
/// - payment and charge must belong to the same (party, property, lease).
/// </summary>
internal sealed class ReceivableApplyPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.ReceivableApply;

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
            : await readers.ReadReceivableApplyHeadAsync(documentId.Value, ct);

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

        return new Snapshot(
            CreditDocumentId: creditDocumentId.Value,
            ChargeDocumentId: chargeDocumentId.Value,
            AppliedOnUtc: appliedOnUtc.Value,
            Amount: amount.Value);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Amount <= 0m)
            throw ReceivableApplyValidationException.AmountMustBePositive(snapshot.Amount);

        if (snapshot.CreditDocumentId == snapshot.ChargeDocumentId)
            throw ReceivableApplyValidationException.PaymentAndChargeMustMatch(snapshot.CreditDocumentId, snapshot.ChargeDocumentId);

        var paymentDocument = await documents.GetAsync(snapshot.CreditDocumentId, ct);
        if (paymentDocument is null)
            throw ReceivableApplyValidationException.PaymentNotFound(snapshot.CreditDocumentId);

        if (!ReceivableCreditSourceResolver.IsCreditSourceDocumentType(paymentDocument.TypeCode))
            throw ReceivableApplyValidationException.PaymentWrongType(snapshot.CreditDocumentId, paymentDocument.TypeCode);

        var chargeDocument = await documents.GetAsync(snapshot.ChargeDocumentId, ct);
        if (chargeDocument is null)
            throw ReceivableApplyValidationException.ChargeNotFound(snapshot.ChargeDocumentId);

        if (!PropertyManagementCodes.IsChargeLikeDocumentType(chargeDocument.TypeCode))
            throw ReceivableApplyValidationException.ChargeWrongType(snapshot.ChargeDocumentId, chargeDocument.TypeCode);

        var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, paymentDocument, ct);
        var charge = await ReadChargeLikeContextAsync(snapshot.ChargeDocumentId, chargeDocument.TypeCode, ct);

        if (creditSource.PartyId != charge.PartyId || creditSource.PropertyId != charge.PropertyId || creditSource.LeaseId != charge.LeaseId)
            throw ReceivableApplyValidationException.PartyPropertyLeaseMismatch(snapshot.CreditDocumentId, snapshot.ChargeDocumentId);
    }

    private async Task<ChargeLikeContext> ReadChargeLikeContextAsync(
        Guid chargeDocumentId,
        string chargeTypeCode,
        CancellationToken ct)
    {
        if (string.Equals(chargeTypeCode, PropertyManagementCodes.ReceivableCharge, StringComparison.OrdinalIgnoreCase))
        {
            var charge = await readers.ReadReceivableChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(charge.PartyId, charge.PropertyId, charge.LeaseId);
        }

        if (string.Equals(chargeTypeCode, PropertyManagementCodes.LateFeeCharge, StringComparison.OrdinalIgnoreCase))
        {
            var charge = await readers.ReadLateFeeChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(charge.PartyId, charge.PropertyId, charge.LeaseId);
        }

        if (string.Equals(chargeTypeCode, PropertyManagementCodes.RentCharge, StringComparison.OrdinalIgnoreCase))
        {
            var charge = await readers.ReadRentChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(charge.PartyId, charge.PropertyId, charge.LeaseId);
        }

        throw ReceivableApplyValidationException.ChargeWrongType(chargeDocumentId, chargeTypeCode);
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

    private readonly record struct ChargeLikeContext(Guid PartyId, Guid PropertyId, Guid LeaseId);
}
