using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal sealed class PayablePaymentPayloadValidator(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementBankAccountReader bankAccounts,
    IPropertyManagementPartyReader parties)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.PayablePayment;

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
            : await readers.ReadPayablePaymentHeadAsync(documentId.Value, ct);

        var fields = payload.Fields;

        TryGetGuid(fields, "party_id", out var partyId);
        TryGetGuid(fields, "property_id", out var propertyId);
        TryGetGuid(fields, "bank_account_id", out var bankAccountId);
        TryGetDecimal(fields, "amount", out var amount);

        partyId ??= current?.PartyId;
        propertyId ??= current?.PropertyId;
        bankAccountId ??= current?.BankAccountId;
        amount ??= current?.Amount;

        if (partyId is null || propertyId is null || amount is null)
            return null;

        return new Snapshot(documentId, partyId.Value, propertyId.Value, bankAccountId, amount.Value);
    }

    private async Task ValidateBusinessRulesAsync(Snapshot snapshot, CancellationToken ct)
    {
        var party = await parties.TryGetAsync(snapshot.PartyId, ct);
        if (party is null)
            throw PayablePaymentValidationException.VendorNotFound(snapshot.PartyId, snapshot.DocumentId);

        if (party.IsDeleted)
            throw PayablePaymentValidationException.VendorDeleted(snapshot.PartyId, snapshot.DocumentId);

        if (!party.IsVendor)
            throw PayablePaymentValidationException.VendorRoleRequired(snapshot.PartyId, snapshot.DocumentId);

        if (snapshot.Amount <= 0m)
            throw PayablePaymentValidationException.AmountMustBePositive(snapshot.Amount, snapshot.DocumentId);

        var property = await readers.ReadPropertyHeadAsync(snapshot.PropertyId, ct);
        if (property is null)
            throw PayablePaymentValidationException.PropertyNotFound(snapshot.PropertyId, snapshot.DocumentId);

        if (property.IsDeleted)
            throw PayablePaymentValidationException.PropertyDeleted(snapshot.PropertyId, snapshot.DocumentId);

        if (snapshot.BankAccountId is { } bankAccountId)
        {
            var bankAccount = await bankAccounts.TryGetAsync(bankAccountId, ct);
            if (bankAccount is null)
                throw PayablePaymentValidationException.BankAccountNotFound(bankAccountId, snapshot.DocumentId);

            if (bankAccount.IsDeleted)
                throw PayablePaymentValidationException.BankAccountDeleted(bankAccountId, snapshot.DocumentId);
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

    private readonly record struct Snapshot(
        Guid? DocumentId,
        Guid PartyId,
        Guid PropertyId,
        Guid? BankAccountId,
        decimal Amount);
}
