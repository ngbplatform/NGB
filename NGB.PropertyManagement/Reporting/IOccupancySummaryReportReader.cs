namespace NGB.PropertyManagement.Reporting;

public sealed record OccupancySummaryContinuation(string BuildingDisplay, Guid BuildingId, int Offset = 0);

public sealed record OccupancySummarySlice(
    IReadOnlyList<OccupancySummaryRow> Rows,
    bool HasMore,
    OccupancySummaryContinuation? Next);

/// <summary>Independent detail and global-summary reads. A page does not require portfolio totals.</summary>
public interface IOccupancySummaryReportReader
{
    Task<OccupancySummarySlice> GetSliceAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        OccupancySummaryContinuation? after,
        int limit,
        CancellationToken ct = default);

    Task<OccupancySummaryTotals> GetTotalsAsync(Guid? buildingId, DateOnly asOfUtc, CancellationToken ct = default);

    IAsyncEnumerable<IReadOnlyList<OccupancySummaryRow>> ReadAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        CancellationToken ct = default);
}
