namespace NGB.PropertyManagement.Contracts.Receivables;

public sealed record ReceivablesFifoApplyExecuteRequest(Guid CreditDocumentId, int? MaxApplications);

public sealed record ReceivablesExecutedApplyDto(Guid ApplyId, Guid ChargeDocumentId, decimal Amount);

public sealed record ReceivablesFifoApplyExecuteResponse(
    Guid CreditDocumentId,
    Guid RegisterId,
    decimal TotalApplied,
    decimal RemainingCredit,
    IReadOnlyList<ReceivablesExecutedApplyDto> ExecutedApplies);
