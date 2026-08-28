namespace NGB.PropertyManagement.Payables;

/// <summary>
/// Persistence boundary for upserting <c>pm.payable_apply</c> typed head fields.
/// </summary>
public interface IPayableApplyHeadWriter
{
    Task UpsertAsync(
        Guid documentId,
        Guid creditDocumentId,
        Guid chargeDocumentId,
        DateOnly appliedOnUtc,
        decimal amount,
        string? memo,
        CancellationToken ct = default);
}

public interface IPayableApplyHeadBatchWriter : IPayableApplyHeadWriter
{
    Task UpsertManyAsync(IReadOnlyList<PayableApplyHeadWrite> items, CancellationToken ct = default);
}

public sealed record PayableApplyHeadWrite(
    Guid DocumentId,
    Guid CreditDocumentId,
    Guid ChargeDocumentId,
    DateOnly AppliedOnUtc,
    decimal Amount,
    string? Memo);
