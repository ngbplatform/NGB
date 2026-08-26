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
    string? LeaseDisplay);

public interface IReceivablesReportReader
{
    Task<ReceivablesReportPage> GetPageAsync(
        Guid registerId,
        Guid leaseId,
        ReceivablesReportMode mode,
        int offset,
        int limit,
        CancellationToken ct = default);
}
