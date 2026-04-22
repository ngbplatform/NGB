using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// Batch UX endpoint for posting a set of pm.receivable_apply documents atomically.
///
/// Production notes:
/// - Single DB transaction (no partial writes).
/// - Deterministic advisory locking for all referenced docs (payments + charges + existing applies).
/// - Per-apply posting handler enforces over-apply / insufficient credit in the same transaction.
/// </summary>
public sealed class ReceivablesApplyBatchService(
    IDocumentDraftService drafts,
    IDocumentPostingService posting,
    IDocumentRelationshipService relationships,
    IPropertyManagementAccountingPolicyReader policyReader,
    IReceivableApplyHeadWriter applyHeadWriter,
    IDocumentRepository documents,
    IAdvisoryLockManager locks,
    IUnitOfWork uow)
    : IReceivablesApplyBatchService
{
    private const int MaxLines = 500;

    public async Task<ReceivablesApplyBatchResponse> ExecuteAsync(
        ReceivablesApplyBatchRequest request,
        CancellationToken ct = default)
    {
        if (request.Applies is null || request.Applies.Count == 0)
            throw ReceivablesApplyBatchValidationException.AppliesMustNotBeEmpty();

        if (request.Applies.Count > MaxLines)
            throw ReceivablesApplyBatchValidationException.AppliesTooLarge(request.Applies.Count, MaxLines);

        var parsed = new List<ApplyItem>(request.Applies.Count);
        foreach (var a in request.Applies)
        {
            if (a is null)
                continue;

            var applyId = a.ApplyId;
            if (applyId == Guid.Empty)
                applyId = null;

            var fields = ParsePayload(a.ApplyPayload);

            if (fields.Amount <= 0m)
                throw ReceivableApplyValidationException.AmountMustBePositive(fields.Amount);

            if (fields.CreditDocumentId == Guid.Empty)
                throw ReceivablesApplyBatchValidationException.PayloadFieldMissing("credit_document_id");

            if (fields.ChargeDocumentId == Guid.Empty)
                throw ReceivablesApplyBatchValidationException.PayloadFieldMissing("charge_document_id");

            if (fields.CreditDocumentId == fields.ChargeDocumentId)
                throw ReceivableApplyValidationException.PaymentAndChargeMustMatch(fields.CreditDocumentId, fields.ChargeDocumentId);

            parsed.Add(new ApplyItem(
                ApplyId: applyId,
                CreditDocumentId: fields.CreditDocumentId,
                ChargeDocumentId: fields.ChargeDocumentId,
                AppliedOnUtc: fields.AppliedOnUtc,
                Amount: fields.Amount,
                Memo: fields.Memo));
        }

        if (parsed.Count == 0)
            throw ReceivablesApplyBatchValidationException.AppliesMustNotBeEmpty();

        // Resolve register id for response (also validates policy existence early).
        var policy = await policyReader.GetRequiredAsync(ct);

        var executed = new List<ReceivablesApplyBatchExecutedItem>(parsed.Count);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // Lock all referenced documents deterministically to avoid deadlocks with other apply flows.
            var docIds = new List<Guid>(parsed.Count * 3);
            docIds.AddRange(parsed.Select(x => x.CreditDocumentId));
            docIds.AddRange(parsed.Select(x => x.ChargeDocumentId));
            docIds.AddRange(parsed.Where(x => x.ApplyId is not null).Select(x => x.ApplyId!.Value));
            await ReceivablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(locks, docIds, innerCt);

            foreach (var a in parsed)
            {
                var createdDraft = a.ApplyId is null;
                var applyId = a.ApplyId ?? await drafts.CreateDraftAsync(
                    typeCode: PropertyManagementCodes.ReceivableApply,
                    number: null,
                    dateUtc: new DateTime(a.AppliedOnUtc.Year, a.AppliedOnUtc.Month, a.AppliedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc),
                    manageTransaction: false,
                    ct: innerCt);

                if (!createdDraft)
                    await EnsureExistingApplyDraftAsync(documents, applyId, innerCt);

                await applyHeadWriter.UpsertAsync(
                    documentId: applyId,
                    creditDocumentId: a.CreditDocumentId,
                    chargeDocumentId: a.ChargeDocumentId,
                    appliedOnUtc: a.AppliedOnUtc,
                    amount: a.Amount,
                    memo: a.Memo,
                    ct: innerCt);

                await ReceivablesApplyExecutionHelpers.EnsureApplyRelationshipsAsync(
                    relationships,
                    applyId,
                    creditDocumentId: a.CreditDocumentId,
                    chargeDocumentId: a.ChargeDocumentId,
                    ct: innerCt);

                await posting.PostAsync(applyId, manageTransaction: false, ct: innerCt);

                executed.Add(new ReceivablesApplyBatchExecutedItem(
                    ApplyId: applyId,
                    CreditDocumentId: a.CreditDocumentId,
                    ChargeDocumentId: a.ChargeDocumentId,
                    AppliedOnUtc: a.AppliedOnUtc,
                    Amount: a.Amount,
                    CreatedDraft: createdDraft));
            }
        }, ct);

        var total = executed.Sum(x => x.Amount);

        return new ReceivablesApplyBatchResponse(
            RegisterId: policy.ReceivablesOpenItemsOperationalRegisterId,
            TotalApplied: total,
            ExecutedApplies: executed);
    }

    private sealed record ApplyItem(
        Guid? ApplyId,
        Guid CreditDocumentId,
        Guid ChargeDocumentId,
        DateOnly AppliedOnUtc,
        decimal Amount,
        string? Memo);

    private static ApplyFields ParsePayload(RecordPayload payload)
    {
        var fields = payload.Fields;
        if (fields is null)
            throw ReceivablesApplyBatchValidationException.PayloadFieldMissing("fields");

        var creditDocumentId = ReadGuid(fields, "credit_document_id");
        var chargeDocumentId = ReadGuid(fields, "charge_document_id");
        var appliedOnUtc = ReadDateOnly(fields, "applied_on_utc");
        var amount = ReadDecimal(fields, "amount");
        var memo = ReadOptionalString(fields, "memo");

        return new ApplyFields(creditDocumentId, chargeDocumentId, appliedOnUtc, amount, memo);
    }

    private sealed record ApplyFields(
        Guid CreditDocumentId,
        Guid ChargeDocumentId,
        DateOnly AppliedOnUtc,
        decimal Amount,
        string? Memo);

    private static Guid ReadGuid(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var el))
            throw ReceivablesApplyBatchValidationException.PayloadFieldMissing(name);

        try
        {
            // Allow both "{guid}" and { id, display }.
            return el.ParseGuidOrRef();
        }
        catch (Exception ex)
        {
            throw ReceivablesApplyBatchValidationException.PayloadFieldInvalid(name, ex.Message);
        }
    }

    private static DateOnly ReadDateOnly(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var el))
            throw ReceivablesApplyBatchValidationException.PayloadFieldMissing(name);

        try
        {
            if (el.ValueKind != JsonValueKind.String)
                throw new NgbArgumentInvalidException(name, "Expected a date string (yyyy-MM-dd).");

            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s))
                throw new NgbArgumentInvalidException(name, "Expected a non-empty date string (yyyy-MM-dd).");

            return DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw ReceivablesApplyBatchValidationException.PayloadFieldInvalid(name, ex.Message);
        }
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var el))
            throw ReceivablesApplyBatchValidationException.PayloadFieldMissing(name);

        try
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDecimal(),
                JsonValueKind.String => decimal.Parse(el.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
                _ => throw new NgbArgumentInvalidException(name, "Expected a number or numeric string.")
            };
        }
        catch (Exception ex)
        {
            throw ReceivablesApplyBatchValidationException.PayloadFieldInvalid(name, ex.Message);
        }
    }

    private static string? ReadOptionalString(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var el))
            return null;

        return el.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => el.GetString(),
            _ => el.ToString()
        };
    }

    private static async Task EnsureExistingApplyDraftAsync(IDocumentRepository documents, Guid applyId, CancellationToken ct)
    {
        var doc = await documents.GetForUpdateAsync(applyId, ct);
        if (doc is null)
            throw ReceivablesApplyBatchValidationException.ApplyNotFound(applyId);

        if (!string.Equals(doc.TypeCode, PropertyManagementCodes.ReceivableApply, StringComparison.Ordinal))
            throw ReceivablesApplyBatchValidationException.ApplyWrongType(applyId, doc.TypeCode);

        if (doc.Status != DocumentStatus.Draft)
            throw ReceivablesApplyBatchValidationException.ApplyNotDraft(applyId);
    }
}
