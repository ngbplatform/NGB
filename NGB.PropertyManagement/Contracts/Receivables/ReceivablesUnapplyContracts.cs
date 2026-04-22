namespace NGB.PropertyManagement.Contracts.Receivables;

/// <summary>
/// Canonical response for the business action "Unapply".
///
/// Semantics:
/// - Unapply is implemented by unposting an existing posted <c>pm.receivable_apply</c> document.
/// - No new business mechanism is introduced here; this is just a receivables-oriented API surface
///   over the existing document lifecycle.
/// </summary>
public sealed record ReceivablesUnapplyResponse(
    Guid ApplyId,
    Guid CreditDocumentId,
    Guid ChargeDocumentId,
    DateOnly AppliedOnUtc,
    decimal UnappliedAmount);
