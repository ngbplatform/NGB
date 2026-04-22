namespace NGB.PropertyManagement.Contracts.Receivables;

public sealed record ReceivablesCustomApplyLine(Guid ChargeDocumentId, decimal Amount);

public sealed record ReceivablesCustomApplyExecuteRequest(
    Guid CreditDocumentId,
    IReadOnlyList<ReceivablesCustomApplyLine> Applies);

public sealed record ReceivablesCustomApplyExecuteResponse(
    Guid CreditDocumentId,
    Guid RegisterId,
    decimal TotalApplied,
    decimal RemainingCredit,
    IReadOnlyList<ReceivablesExecutedApplyDto> ExecutedApplies);
