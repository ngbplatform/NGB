namespace NGB.PropertyManagement.Reporting;

public interface IBuildingSummaryReader
{
    Task<BuildingSummary> GetSummaryAsync(Guid buildingId, DateOnly asOfUtc, CancellationToken ct = default);
}