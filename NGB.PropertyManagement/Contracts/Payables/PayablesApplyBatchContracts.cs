using NGB.Contracts.Common;

namespace NGB.PropertyManagement.Contracts.Payables;

public sealed record PayablesApplyBatchRequest(IReadOnlyList<PayablesApplyBatchItem> Applies);

public sealed record PayablesApplyBatchItem(Guid? ApplyId, RecordPayload ApplyPayload);

public sealed record PayablesApplyBatchResponse(
    Guid RegisterId,
    decimal TotalApplied,
    IReadOnlyList<PayablesApplyBatchExecutedItem> ExecutedApplies);

public sealed record PayablesApplyBatchExecutedItem(
    Guid ApplyId,
    Guid CreditDocumentId,
    Guid ChargeDocumentId,
    DateOnly AppliedOnUtc,
    decimal Amount,
    bool CreatedDraft);
