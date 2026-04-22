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

/// <summary>
/// Draft-time domain validation for pm.rent_charge.
///
/// The generic document layer already validates required fields and basic JSON types.
/// This validator adds business rules specific to rent charges:
/// - lease_id must reference an existing pm.lease document that is not marked for deletion;
/// - amount must be positive;
/// - period_from_utc must be on or before period_to_utc;
/// - charge period must stay within the lease term.
/// </summary>
internal sealed class RentChargePayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.RentCharge;

    public async Task ValidateCreateDraftPayloadAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        var snapshot = await ResolveSnapshotAsync(documentId: null, payload, ct);
        if (snapshot is null)
            return;

        ValidateBusinessRules(snapshot.Value);
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

        ValidateBusinessRules(snapshot.Value);
    }

    private async Task<Snapshot?> ResolveSnapshotAsync(Guid? documentId, RecordPayload payload, CancellationToken ct)
    {
        var current = documentId is null
            ? null
            : await readers.ReadRentChargeHeadAsync(documentId.Value, ct);
        
        var fields = payload.Fields;

        TryGetGuid(fields, "lease_id", out var leaseId);
        TryGetDate(fields, "period_from_utc", out var fromInclusive);
        TryGetDate(fields, "period_to_utc", out var toInclusive);
        TryGetDecimal(fields, "amount", out var amount);

        leaseId ??= current?.LeaseId;
        fromInclusive ??= current?.PeriodFromUtc;
        toInclusive ??= current?.PeriodToUtc;
        amount ??= current?.Amount;

        if (leaseId is null || fromInclusive is null || toInclusive is null || amount is null)
            return null;

        var leaseDocument = await documents.GetAsync(leaseId.Value, ct);
        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
        {
            throw RentChargeValidationException.LeaseNotFound(leaseId.Value);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw RentChargeValidationException.LeaseMarkedForDeletion(leaseId.Value);

        var lease = await readers.ReadLeaseHeadAsync(leaseId.Value, ct);

        return new Snapshot(
            LeaseId: leaseId.Value,
            PeriodFromUtc: fromInclusive.Value,
            PeriodToUtc: toInclusive.Value,
            Amount: amount.Value,
            LeaseStartOnUtc: lease.StartOnUtc,
            LeaseEndOnUtc: lease.EndOnUtc);
    }

    private static void ValidateBusinessRules(Snapshot snapshot)
    {
        if (snapshot.Amount <= 0m)
            throw RentChargeValidationException.AmountMustBePositive(snapshot.Amount);

        if (snapshot.PeriodFromUtc > snapshot.PeriodToUtc)
            throw RentChargeValidationException.PeriodRangeInvalid(snapshot.PeriodFromUtc, snapshot.PeriodToUtc);

        if (snapshot.PeriodFromUtc < snapshot.LeaseStartOnUtc)
            throw RentChargeValidationException.PeriodOutsideLeaseTerm(
                snapshot.PeriodFromUtc,
                snapshot.PeriodToUtc,
                snapshot.LeaseStartOnUtc,
                snapshot.LeaseEndOnUtc);

        if (snapshot.LeaseEndOnUtc is not null && snapshot.PeriodToUtc > snapshot.LeaseEndOnUtc.Value)
            throw RentChargeValidationException.PeriodOutsideLeaseTerm(
                snapshot.PeriodFromUtc,
                snapshot.PeriodToUtc,
                snapshot.LeaseStartOnUtc,
                snapshot.LeaseEndOnUtc);
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
        Guid LeaseId,
        DateOnly PeriodFromUtc,
        DateOnly PeriodToUtc,
        decimal Amount,
        DateOnly LeaseStartOnUtc,
        DateOnly? LeaseEndOnUtc);
}
