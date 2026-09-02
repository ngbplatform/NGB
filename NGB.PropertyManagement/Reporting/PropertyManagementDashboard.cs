namespace NGB.PropertyManagement.Reporting;

public interface IPropertyManagementDashboardReader
{
    Task<PropertyManagementDashboardSnapshot> GetAsync(DateOnly asOfUtc, CancellationToken ct = default);
}

public sealed record PropertyManagementDashboardSnapshot(
    PropertyManagementDashboardPortfolioSnapshot Portfolio,
    PropertyManagementDashboardLeaseSnapshot Leases,
    IReadOnlyList<PropertyManagementDashboardOccupancySnapshot> OccupancyTrend,
    IReadOnlyList<PropertyManagementDashboardCollectionsSnapshot> CollectionsTrend);

public sealed record PropertyManagementDashboardPortfolioSnapshot(
    int BuildingCount,
    int TotalUnits,
    int OccupiedUnits,
    int FutureOccupiedUnits);

public sealed record PropertyManagementDashboardLeaseSnapshot(
    int Expiring30Count,
    int UpcomingMoveInCount,
    int UpcomingMoveOutCount,
    IReadOnlyList<PropertyManagementDashboardLeaseEventSnapshot> Events);

public sealed record PropertyManagementDashboardLeaseEventSnapshot(
    string Kind,
    DateOnly Date,
    Guid LeaseId,
    string LeaseDisplay,
    string PropertyDisplay);

public sealed record PropertyManagementDashboardOccupancySnapshot(DateOnly Month, int OccupiedUnits, int VacantUnits);

public sealed record PropertyManagementDashboardCollectionsSnapshot(DateOnly Month, decimal Billed, decimal Collected);
