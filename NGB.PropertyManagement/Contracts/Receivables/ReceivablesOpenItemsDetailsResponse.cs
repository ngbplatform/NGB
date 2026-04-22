namespace NGB.PropertyManagement.Contracts.Receivables;

public sealed record ReceivablesOpenItemsDetailsResponse(
    Guid RegisterId,
    Guid PartyId,
    string? PartyDisplay,
    Guid PropertyId,
    string? PropertyDisplay,
    Guid LeaseId,
    string? LeaseDisplay,
    IReadOnlyList<ReceivablesOpenChargeItemDetailsDto> Charges,
    IReadOnlyList<ReceivablesOpenCreditItemDetailsDto> Credits,
    IReadOnlyList<ReceivablesAllocationDetailsDto> Allocations,
    decimal TotalOutstanding,
    decimal TotalCredit);

public sealed record ReceivablesOpenChargeItemDetailsDto(
    Guid ChargeDocumentId,
    string DocumentType,
    string? Number,
    string? ChargeDisplay,
    DateOnly DueOnUtc,
    Guid? ChargeTypeId,
    string? ChargeTypeDisplay,
    string? Memo,
    decimal OriginalAmount,
    decimal OutstandingAmount);

public sealed record ReceivablesOpenCreditItemDetailsDto(
    Guid CreditDocumentId,
    string DocumentType,
    string? Number,
    string? CreditDocumentDisplay,
    DateOnly ReceivedOnUtc,
    string? Memo,
    decimal OriginalAmount,
    decimal AvailableCredit);

public sealed record ReceivablesAllocationDetailsDto(
    Guid ApplyId,
    string? ApplyDisplay,
    string? ApplyNumber,
    Guid CreditDocumentId,
    string CreditDocumentType,
    string? CreditDocumentDisplay,
    string? CreditDocumentNumber,
    Guid ChargeDocumentId,
    string ChargeDocumentType,
    string? ChargeDisplay,
    string? ChargeNumber,
    DateOnly AppliedOnUtc,
    decimal Amount,
    bool IsPosted);
