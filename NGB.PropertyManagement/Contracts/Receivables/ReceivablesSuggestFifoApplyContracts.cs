using NGB.Contracts.Common;

namespace NGB.PropertyManagement.Contracts.Receivables;

/// <summary>
/// UI-oriented FIFO suggestion across all open charge and credit-source items for a lease.
///
/// Default mode is read-only (no writes). If <see cref="CreateDrafts"/> is true, the service
/// creates pm.receivable_apply draft documents for the suggested allocations (still not posted).
/// </summary>
public sealed record ReceivablesSuggestFifoApplyRequest(
    Guid LeaseId,
    Guid? PartyId = null,
    Guid? PropertyId = null,
    DateOnly? AsOfMonth = null,
    DateOnly? ToMonth = null,
    int? Limit = null,
    bool CreateDrafts = false);

public sealed record ReceivablesSuggestFifoApplyResponse(
    Guid RegisterId,
    Guid PartyId,
    string? PartyDisplay,
    Guid PropertyId,
    string? PropertyDisplay,
    Guid LeaseId,
    string? LeaseDisplay,
    decimal TotalOutstanding,
    decimal TotalCredit,
    decimal TotalApplied,
    decimal RemainingOutstanding,
    decimal RemainingCredit,
    IReadOnlyList<ReceivablesSuggestedLeaseApplyDto> SuggestedApplies,
    IReadOnlyList<ReceivablesApplyWarningDto> Warnings);

public sealed record ReceivablesSuggestedLeaseApplyDto(
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

public sealed record ReceivablesApplyWarningDto(string Code, string Message);
