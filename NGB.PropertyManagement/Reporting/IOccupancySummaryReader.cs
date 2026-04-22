namespace NGB.PropertyManagement.Reporting;

public interface IOccupancySummaryReader
{
    Task<OccupancySummaryPage> GetPageAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        int offset,
        int limit,
        CancellationToken ct = default);
}
