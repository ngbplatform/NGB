using NGB.Contracts.Common;

namespace NGB.PropertyManagement.Contracts.Receivables;

public sealed record ReceivablesFifoApplySuggestRequest(Guid CreditDocumentId, int? MaxApplications);

public sealed record ReceivablesFifoApplySuggestResponse(
    Guid CreditDocumentId,
    Guid RegisterId,
    decimal AvailableCredit,
    decimal TotalOutstanding,
    decimal TotalApplied,
    decimal RemainingCredit,
    IReadOnlyList<ReceivablesSuggestedApplyDto> SuggestedApplies);

public sealed record ReceivablesSuggestedApplyDto(
    Guid ChargeDocumentId,
    decimal ChargeOutstandingBefore,
    DateOnly ChargeDueOnUtc,
    decimal Amount,
    RecordPayload ApplyPayload);
