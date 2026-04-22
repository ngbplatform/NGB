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

internal sealed class LateFeeChargePayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.LateFeeCharge;

    public async Task ValidateCreateDraftPayloadAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        var snapshot = await ResolveSnapshotAsync(documentId: null, payload, ct);
        if (snapshot is null)
            return;

        if (snapshot.Value.Amount <= 0m)
            throw LateFeeChargeValidationException.AmountMustBePositive(snapshot.Value.Amount, snapshot.Value.DocumentId);
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

        if (snapshot.Value.Amount <= 0m)
            throw LateFeeChargeValidationException.AmountMustBePositive(snapshot.Value.Amount, snapshot.Value.DocumentId);
    }

    private async Task<Snapshot?> ResolveSnapshotAsync(Guid? documentId, RecordPayload payload, CancellationToken ct)
    {
        var current = documentId is null
            ? null
            : await readers.ReadLateFeeChargeHeadAsync(documentId.Value, ct);

        var fields = payload.Fields;

        TryGetGuid(fields, "lease_id", out var leaseId);
        TryGetDecimal(fields, "amount", out var amount);

        leaseId ??= current?.LeaseId;
        amount ??= current?.Amount;

        if (leaseId is null || amount is null)
            return null;

        var leaseDocument = await documents.GetAsync(leaseId.Value, ct);
        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
        {
            throw LateFeeChargeValidationException.LeaseNotFound(leaseId.Value, documentId);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw LateFeeChargeValidationException.LeaseMarkedForDeletion(leaseId.Value, documentId);

        return new Snapshot(documentId, leaseId.Value, amount.Value);
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

    private readonly record struct Snapshot(Guid? DocumentId, Guid LeaseId, decimal Amount);
}
