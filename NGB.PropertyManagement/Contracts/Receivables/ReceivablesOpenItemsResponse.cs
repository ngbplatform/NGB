namespace NGB.PropertyManagement.Contracts.Receivables;

public sealed record ReceivablesOpenItemsResponse(
    Guid RegisterId,
    IReadOnlyList<ReceivablesOpenItemDto> Charges,
    IReadOnlyList<ReceivablesOpenItemDto> Credits,
    decimal TotalOutstanding,
    decimal TotalCredit);

public sealed record ReceivablesOpenItemDto(
    Guid ItemId,
    string? ItemDisplay,
    decimal Amount,
    string? DocumentType = null);
