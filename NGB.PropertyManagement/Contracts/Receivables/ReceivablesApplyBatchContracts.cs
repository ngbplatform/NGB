using NGB.Contracts.Common;

namespace NGB.PropertyManagement.Contracts.Receivables;

/// <summary>
/// Batch UX endpoint for posting a set of pm.receivable_apply documents atomically.
///
/// Semantics:
/// - Each item contains an apply payload (the same shape as returned by suggest endpoints).
/// - If <see cref="ReceivablesApplyBatchItem.ApplyId"/> is provided, the service updates the typed head
///   and posts the existing Draft document.
/// - If it is not provided, the service creates a Draft document, writes the typed head and posts it.
/// - Entire batch is executed in a single DB transaction (no partial effects).
/// </summary>
public sealed record ReceivablesApplyBatchRequest(IReadOnlyList<ReceivablesApplyBatchItem> Applies);

public sealed record ReceivablesApplyBatchItem(Guid? ApplyId, RecordPayload ApplyPayload);

public sealed record ReceivablesApplyBatchResponse(
    Guid RegisterId,
    decimal TotalApplied,
    IReadOnlyList<ReceivablesApplyBatchExecutedItem> ExecutedApplies);

public sealed record ReceivablesApplyBatchExecutedItem(
    Guid ApplyId,
    Guid CreditDocumentId,
    Guid ChargeDocumentId,
    DateOnly AppliedOnUtc,
    decimal Amount,
    bool CreatedDraft);
