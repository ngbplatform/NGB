using NGB.Contracts.Common;

namespace NGB.Trade.Contracts;

public sealed record TradeDocumentLineDefaultsRequestDto(
    string DocumentType,
    string? AsOfDate,
    Guid? WarehouseId,
    Guid? PriceTypeId,
    Guid? SalesInvoiceId,
    Guid? PurchaseReceiptId,
    IReadOnlyList<TradeDocumentLineDefaultsRowRequestDto> Rows);

public sealed record TradeDocumentLineDefaultsRowRequestDto(
    string RowKey,
    Guid ItemId,
    Guid? PriceTypeId);

public sealed record TradeDocumentLineDefaultsResponseDto(
    IReadOnlyList<TradeDocumentLineDefaultsRowResultDto> Rows);

public sealed record TradeDocumentLineDefaultsRowResultDto(
    string RowKey,
    RefValueDto? PriceType,
    decimal? UnitPrice,
    string? Currency,
    decimal? UnitCost);
