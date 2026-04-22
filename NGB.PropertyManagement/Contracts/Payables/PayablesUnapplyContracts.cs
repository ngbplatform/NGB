namespace NGB.PropertyManagement.Contracts.Payables;

public sealed record PayablesUnapplyResponse(
    Guid ApplyId,
    Guid CreditDocumentId,
    Guid ChargeDocumentId,
    DateOnly AppliedOnUtc,
    decimal UnappliedAmount);
