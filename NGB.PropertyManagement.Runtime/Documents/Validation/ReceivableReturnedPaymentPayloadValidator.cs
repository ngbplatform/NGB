using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal sealed class ReceivableReturnedPaymentPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.ReceivableReturnedPayment;

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
            : await readers.ReadReceivableReturnedPaymentHeadAsync(documentId.Value, ct);
        var fields = payload.Fields;

        TryGetGuid(fields, "original_payment_id", out var originalPaymentId);
        TryGetDecimal(fields, "amount", out var amount);
        TryGetDateOnly(fields, "returned_on_utc", out var returnedOnUtc);

        originalPaymentId ??= current?.OriginalPaymentId;
        amount ??= current?.Amount;
        returnedOnUtc ??= current?.ReturnedOnUtc;

        if (originalPaymentId is null || amount is null || returnedOnUtc is null)
            return null;

        var originalPaymentDocument = await documents.GetAsync(originalPaymentId.Value, ct);
        if (originalPaymentDocument is null
            || !string.Equals(originalPaymentDocument.TypeCode, PropertyManagementCodes.ReceivablePayment,
                StringComparison.OrdinalIgnoreCase))
        {
            throw ReceivableReturnedPaymentValidationException.OriginalPaymentNotFound(originalPaymentId.Value, documentId);
        }

        return new Snapshot(documentId, originalPaymentId.Value, returnedOnUtc.Value, amount.Value);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Amount <= 0m)
            throw ReceivableReturnedPaymentValidationException.AmountMustBePositive(snapshot.Amount, snapshot.DocumentId);

        var originalPayment = await readers.ReadReceivablePaymentHeadAsync(snapshot.OriginalPaymentId, ct);
        var leaseDocument = await documents.GetAsync(originalPayment.LeaseId, ct);
        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease,
                StringComparison.OrdinalIgnoreCase))
        {
            throw ReceivableReturnedPaymentValidationException.LeaseNotFound(originalPayment.LeaseId, snapshot.DocumentId);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw ReceivableReturnedPaymentValidationException.LeaseMarkedForDeletion(originalPayment.LeaseId, snapshot.DocumentId);

        if (snapshot.ReturnedOnUtc < originalPayment.ReceivedOnUtc)
        {
            throw ReceivableReturnedPaymentValidationException.ReturnedOnBeforeOriginalPayment(
                originalPaymentId: snapshot.OriginalPaymentId,
                originalReceivedOnUtc: originalPayment.ReceivedOnUtc,
                returnedOnUtc: snapshot.ReturnedOnUtc,
                documentId: snapshot.DocumentId);
        }
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

    private static bool TryGetDateOnly(IReadOnlyDictionary<string, JsonElement>? fields, string key, out DateOnly? value)
    {
        value = null;
        if (fields is null || !fields.TryGetValue(key, out var el))
            return false;

        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        try
        {
            value = el.ValueKind == JsonValueKind.String
                ? DateOnly.Parse(el.GetString() ?? string.Empty, CultureInfo.InvariantCulture)
                : DateOnly.Parse(el.ToString(), CultureInfo.InvariantCulture);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct Snapshot(
        Guid? DocumentId,
        Guid OriginalPaymentId,
        DateOnly ReturnedOnUtc,
        decimal Amount);
}
