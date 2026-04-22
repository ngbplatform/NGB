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

internal sealed class ReceivableCreditMemoPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.ReceivableCreditMemo;

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
            : await readers.ReadReceivableCreditMemoHeadAsync(documentId.Value, ct);
        var fields = payload.Fields;

        TryGetGuid(fields, "lease_id", out var leaseId);
        TryGetGuid(fields, "charge_type_id", out var chargeTypeId);
        TryGetDecimal(fields, "amount", out var amount);

        leaseId ??= current?.LeaseId;
        chargeTypeId ??= current?.ChargeTypeId;
        amount ??= current?.Amount;

        if (leaseId is null || chargeTypeId is null || amount is null)
            return null;

        var leaseDocument = await documents.GetAsync(leaseId.Value, ct);
        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
        {
            throw ReceivableCreditMemoValidationException.LeaseNotFound(leaseId.Value, documentId);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw ReceivableCreditMemoValidationException.LeaseMarkedForDeletion(leaseId.Value, documentId);

        try
        {
            await readers.ReadChargeTypeHeadAsync(chargeTypeId.Value, ct);
        }
        catch (InvalidOperationException)
        {
            throw ReceivableCreditMemoValidationException.ChargeTypeNotFound(chargeTypeId.Value, documentId);
        }

        return new Snapshot(documentId, leaseId.Value, chargeTypeId.Value, amount.Value);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Amount <= 0m)
            throw ReceivableCreditMemoValidationException.AmountMustBePositive(snapshot.Amount, snapshot.DocumentId);

        var lease = await readers.ReadLeaseHeadAsync(snapshot.LeaseId, ct);
        var property = await readers.ReadPropertyHeadAsync(lease.PropertyId, ct);
        
        if (property is null)
            throw new DocumentPropertyNotFoundException(TypeCode, lease.PropertyId);

        if (property.IsDeleted)
            throw new DocumentPropertyDeletedException(TypeCode, lease.PropertyId);

        if (!string.Equals(property.Kind, "Unit", StringComparison.OrdinalIgnoreCase))
            throw new DocumentPropertyMustBeUnitException(TypeCode, lease.PropertyId, property.Kind);
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

    private readonly record struct Snapshot(Guid? DocumentId, Guid LeaseId, Guid ChargeTypeId, decimal Amount);
}
