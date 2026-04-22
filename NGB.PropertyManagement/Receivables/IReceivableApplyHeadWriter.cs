namespace NGB.PropertyManagement.Receivables;

/// <summary>
/// Persistence boundary for upserting <c>pm.receivable_apply</c> typed head fields.
///
/// Notes:
/// - Draft creation already ensures the typed head row exists (INSERT ... ON CONFLICT DO NOTHING).
/// - Therefore implementations should UPSERT into <c>doc_pm_receivable_apply</c>.
/// </summary>
public interface IReceivableApplyHeadWriter
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
