namespace NGB.PropertyManagement.Reporting;

public enum ReceivablesReportMode
{
    OpenItemsDetails = 0,
    Aging = 1
}

public sealed record ReceivablesReportRow(
    bool IsCharge,
    Guid DocumentId,
    string DocumentType,
    string? Display,
    DateOnly? DueOnUtc,
    DateOnly? ReceivedOnUtc,
    string? ChargeTypeDisplay,
    decimal OriginalAmount,
    decimal OpenAmount);

public sealed record ReceivablesReportPage(
    IReadOnlyList<ReceivablesReportRow> Rows,
    int Total,
    decimal TotalOriginal,
    decimal TotalOutstanding,
    decimal TotalCredit,
    string? PartyDisplay,
    string? PropertyDisplay,
    string? LeaseDisplay,
    bool HasMore = false,
    int? NextAfterKindOrder = null,
    DateOnly? NextAfterSortDate = null,
    Guid? NextAfterDocumentId = null);

public sealed record ReceivablesReportPageCursor(
    int Offset,
    int Total,
    decimal TotalOriginal,
    decimal TotalOutstanding,
    decimal TotalCredit,
    string? PartyDisplay,
    string? PropertyDisplay,
    string? LeaseDisplay,
    int? AfterKindOrder = null,
    DateOnly? AfterSortDate = null,
    Guid? AfterDocumentId = null);

public interface IReceivablesReportReader
{
    Task<ReceivablesReportPage> GetPageAsync(
        Guid registerId,
        Guid leaseId,
        ReceivablesReportMode mode,
        int offset,
        int limit,
        CancellationToken ct = default);

    async Task<ReceivablesReportPage> GetCursorPageAsync(
        Guid registerId,
        Guid leaseId,
        ReceivablesReportMode mode,
        ReceivablesReportPageCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var offset = cursor?.Offset ?? 0;
        var page = await GetPageAsync(registerId, leaseId, mode, offset, limit, ct);
        return page with { HasMore = offset + page.Rows.Count < page.Total };
    }
}
