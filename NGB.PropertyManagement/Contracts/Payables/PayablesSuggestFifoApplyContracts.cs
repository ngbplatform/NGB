using NGB.Contracts.Common;

namespace NGB.PropertyManagement.Contracts.Payables;

public sealed record PayablesSuggestFifoApplyRequest(
    Guid PartyId,
    Guid PropertyId,
    DateOnly? AsOfMonth = null,
    DateOnly? ToMonth = null,
    int? Limit = null,
    bool CreateDrafts = false);

public sealed record PayablesSuggestFifoApplyResponse(
    Guid RegisterId,
    Guid VendorId,
    string? VendorDisplay,
    Guid PropertyId,
    string? PropertyDisplay,
    decimal TotalOutstanding,
    decimal TotalCredit,
    decimal TotalApplied,
    decimal RemainingOutstanding,
    decimal RemainingCredit,
    IReadOnlyList<PayablesSuggestedApplyDto> SuggestedApplies,
    IReadOnlyList<PayablesApplyWarningDto> Warnings);

public sealed record PayablesSuggestedApplyDto(
    Guid? ApplyId,
    Guid CreditDocumentId,
    string CreditDocumentType,
    string? CreditDocumentDisplay,
    DateOnly CreditDocumentDateUtc,
    decimal CreditAmountBefore,
    decimal CreditAmountAfter,
    Guid ChargeDocumentId,
    string? ChargeDisplay,
    DateOnly ChargeDueOnUtc,
    decimal ChargeOutstandingBefore,
    decimal ChargeOutstandingAfter,
    decimal Amount,
    RecordPayload ApplyPayload);

public sealed record PayablesApplyWarningDto(string Code, string Message);
