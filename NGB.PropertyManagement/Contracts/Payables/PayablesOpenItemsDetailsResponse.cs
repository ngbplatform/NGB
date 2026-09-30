namespace NGB.PropertyManagement.Contracts.Payables;

public sealed record PayablesOpenItemsDetailsResponse(
    Guid RegisterId,
    Guid VendorId,
    string? VendorDisplay,
    Guid PropertyId,
    string? PropertyDisplay,
    IReadOnlyList<PayablesOpenChargeItemDetailsDto> Charges,
    IReadOnlyList<PayablesOpenCreditItemDetailsDto> Credits,
    IReadOnlyList<PayablesAllocationDetailsDto> Allocations,
    decimal TotalOutstanding,
    decimal TotalCredit,
    int? ChargeCount = null,
    int? CreditCount = null,
    int? AllocationCount = null,
    int? ChargeOffset = null,
    int? CreditOffset = null,
    int? AllocationOffset = null,
    int? Limit = null,
    bool? ChargesHaveMore = null,
    bool? CreditsHaveMore = null,
    bool? AllocationsHaveMore = null);

public sealed record PayablesOpenChargeItemDetailsDto(
    Guid ChargeDocumentId,
    string DocumentType,
    string? Number,
    string? ChargeDisplay,
    DateOnly DueOnUtc,
    Guid? ChargeTypeId,
    string? ChargeTypeDisplay,
    string? VendorInvoiceNo,
    string? Memo,
    decimal OriginalAmount,
    decimal OutstandingAmount);

public sealed record PayablesOpenCreditItemDetailsDto(
    Guid CreditDocumentId,
    string DocumentType,
    string? Number,
    string? CreditDocumentDisplay,
    DateOnly CreditDocumentDateUtc,
    string? Memo,
    decimal OriginalAmount,
    decimal AvailableCredit);

public sealed record PayablesAllocationDetailsDto(
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
