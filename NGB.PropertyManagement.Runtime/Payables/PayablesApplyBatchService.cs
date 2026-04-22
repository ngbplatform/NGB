using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Payables;

public sealed class PayablesApplyBatchService(
    IDocumentDraftService drafts,
    IDocumentPostingService posting,
    IDocumentRelationshipService relationships,
    IPropertyManagementAccountingPolicyReader policyReader,
    IPayableApplyHeadWriter applyHeadWriter,
    IDocumentRepository documents,
    IAdvisoryLockManager locks,
    IUnitOfWork uow)
    : IPayablesApplyBatchService
{
    private const int MaxLines = 500;

    public async Task<PayablesApplyBatchResponse> ExecuteAsync(
        PayablesApplyBatchRequest request,
        CancellationToken ct = default)
    {
        if (request.Applies is null || request.Applies.Count == 0)
            throw PayablesApplyBatchValidationException.AppliesMustNotBeEmpty();

        if (request.Applies.Count > MaxLines)
            throw PayablesApplyBatchValidationException.AppliesTooLarge(request.Applies.Count, MaxLines);

        var parsed = new List<ApplyItem>(request.Applies.Count);
        foreach (var a in request.Applies)
        {
            if (a is null)
                continue;

            var applyId = a.ApplyId == Guid.Empty ? null : a.ApplyId;
            var fields = ParsePayload(a.ApplyPayload);

            if (fields.Amount <= 0m)
                throw PayableApplyValidationException.AmountMustBePositive(fields.Amount);

            if (fields.CreditDocumentId == Guid.Empty)
                throw PayablesApplyBatchValidationException.PayloadFieldMissing("credit_document_id");

            if (fields.ChargeDocumentId == Guid.Empty)
                throw PayablesApplyBatchValidationException.PayloadFieldMissing("charge_document_id");

            if (fields.CreditDocumentId == fields.ChargeDocumentId)
                throw PayableApplyValidationException.CreditSourceAndChargeMustDiffer(fields.CreditDocumentId, fields.ChargeDocumentId);

            parsed.Add(new ApplyItem(applyId, fields.CreditDocumentId, fields.ChargeDocumentId, fields.AppliedOnUtc, fields.Amount, fields.Memo));
        }

        if (parsed.Count == 0)
            throw PayablesApplyBatchValidationException.AppliesMustNotBeEmpty();

        var policy = await policyReader.GetRequiredAsync(ct);
        var executed = new List<PayablesApplyBatchExecutedItem>(parsed.Count);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var docIds = new List<Guid>(parsed.Count * 3);
            docIds.AddRange(parsed.Select(x => x.CreditDocumentId));
            docIds.AddRange(parsed.Select(x => x.ChargeDocumentId));
            docIds.AddRange(parsed.Where(x => x.ApplyId is not null).Select(x => x.ApplyId!.Value));

            await PayablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(locks, docIds, innerCt);

            foreach (var a in parsed)
            {
                var createdDraft = a.ApplyId is null;
                var applyId = a.ApplyId ?? await drafts.CreateDraftAsync(
                    PropertyManagementCodes.PayableApply,
                    number: null,
                    dateUtc: new DateTime(a.AppliedOnUtc.Year, a.AppliedOnUtc.Month, a.AppliedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc),
                    manageTransaction: false,
                    ct: innerCt);

                if (!createdDraft)
                    await EnsureExistingApplyDraftAsync(documents, applyId, innerCt);

                await applyHeadWriter.UpsertAsync(
                    applyId,
                    a.CreditDocumentId,
                    a.ChargeDocumentId,
                    a.AppliedOnUtc,
                    a.Amount,
                    a.Memo,
                    innerCt);
                
                await PayablesApplyExecutionHelpers.EnsureApplyRelationshipsAsync(
                    relationships,
                    applyId,
                    a.CreditDocumentId,
                    a.ChargeDocumentId,
                    innerCt);
                
                await posting.PostAsync(applyId, manageTransaction: false, ct: innerCt);

                executed.Add(new PayablesApplyBatchExecutedItem(
                    applyId,
                    a.CreditDocumentId,
                    a.ChargeDocumentId,
                    a.AppliedOnUtc,
                    a.Amount,
                    createdDraft));
            }
        }, ct);

        return new PayablesApplyBatchResponse(
            policy.PayablesOpenItemsOperationalRegisterId,
            executed.Sum(x => x.Amount),
            executed);
    }

    private static ApplyFields ParsePayload(RecordPayload payload)
    {
        var fields = payload.Fields;
        if (fields is null)
            throw PayablesApplyBatchValidationException.PayloadFieldMissing("fields");

        var creditDocumentId = ReadGuid(fields, "credit_document_id");
        var chargeDocumentId = ReadGuid(fields, "charge_document_id");
        var appliedOnUtc = ReadDateOnly(fields, "applied_on_utc");
        var amount = ReadDecimal(fields, "amount");
        var memo = ReadOptionalString(fields, "memo");

        return new ApplyFields(creditDocumentId, chargeDocumentId, appliedOnUtc, amount, memo);
    }

    private static Guid ReadGuid(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var el))
            throw PayablesApplyBatchValidationException.PayloadFieldMissing(name);

        try
        {
            return el.ParseGuidOrRef();
        }
        catch (Exception ex)
        {
            throw PayablesApplyBatchValidationException.PayloadFieldInvalid(name, ex.Message);
        }
    }

    private static DateOnly ReadDateOnly(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var el))
            throw PayablesApplyBatchValidationException.PayloadFieldMissing(name);

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
            throw PayablesApplyBatchValidationException.PayloadFieldInvalid(name, ex.Message);
        }
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var el))
            throw PayablesApplyBatchValidationException.PayloadFieldMissing(name);

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
            throw PayablesApplyBatchValidationException.PayloadFieldInvalid(name, ex.Message);
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
            throw PayablesApplyBatchValidationException.ApplyNotFound(applyId);

        if (!string.Equals(doc.TypeCode, PropertyManagementCodes.PayableApply, StringComparison.Ordinal))
            throw PayablesApplyBatchValidationException.ApplyWrongType(applyId, doc.TypeCode);

        if (doc.Status != DocumentStatus.Draft)
            throw PayablesApplyBatchValidationException.ApplyNotDraft(applyId);
    }

    private sealed record ApplyFields(
        Guid CreditDocumentId,
        Guid ChargeDocumentId,
        DateOnly AppliedOnUtc,
        decimal Amount,
        string? Memo);

    private sealed record ApplyItem(
        Guid? ApplyId,
        Guid CreditDocumentId,
        Guid ChargeDocumentId,
        DateOnly AppliedOnUtc,
        decimal Amount,
        string? Memo);
}
