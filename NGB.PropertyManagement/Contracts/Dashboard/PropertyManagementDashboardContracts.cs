namespace NGB.PropertyManagement.Contracts.Dashboard;

public interface IPropertyManagementDashboardService
{
    Task<PropertyManagementDashboardResponse> GetAsync(DateOnly asOfUtc, CancellationToken ct = default);
}

public sealed record PropertyManagementDashboardResponse(
    DateOnly AsOfUtc,
    IReadOnlyList<string> Warnings,
    PropertyManagementDashboardPortfolio Portfolio,
    PropertyManagementDashboardLeases Leases,
    PropertyManagementDashboardReceivables Receivables,
    PropertyManagementDashboardMaintenance Maintenance,
    PropertyManagementDashboardPeriods Periods,
    IReadOnlyList<PropertyManagementDashboardOccupancyPoint> OccupancyTrend,
    IReadOnlyList<PropertyManagementDashboardCollectionsPoint> CollectionsTrend);

public sealed record PropertyManagementDashboardPortfolio(
    int BuildingCount,
    int TotalUnits,
    int OccupiedUnits,
    int VacantUnits,
    decimal OccupancyPercent,
    int FutureOccupiedUnits,
    decimal FutureOccupancyPercent);

public sealed record PropertyManagementDashboardLeases(
    int Expiring30Count,
    int UpcomingMoveInCount,
    int UpcomingMoveOutCount,
    IReadOnlyList<PropertyManagementDashboardLeaseEvent> Events);

public sealed record PropertyManagementDashboardLeaseEvent(
    string Kind,
    DateOnly Date,
    Guid LeaseId,
    string LeaseDisplay,
    string PropertyDisplay);

public sealed record PropertyManagementDashboardReceivables(
    decimal TotalOpenItemsNet,
    decimal TotalDiff,
    int RowCount,
    int MismatchRowCount,
    IReadOnlyList<PropertyManagementDashboardMismatch> Mismatches,
    decimal CurrentMonthBilled,
    decimal CurrentMonthCollected);

public sealed record PropertyManagementDashboardMismatch(
    Guid PartyId,
    Guid PropertyId,
    Guid LeaseId,
    string LeaseDisplay,
    string PropertyDisplay,
    string RowKind,
    decimal Diff);

public sealed record PropertyManagementDashboardMaintenance(
    int OpenItemCount,
    int OverdueCount,
    IReadOnlyList<PropertyManagementDashboardMaintenanceItem> Items,
    PropertyManagementDashboardMaintenanceAging Aging);

public sealed record PropertyManagementDashboardMaintenanceItem(
    Guid RequestId,
    Guid? WorkOrderId,
    string QueueState,
    string Subject,
    string RequestDisplay,
    string PropertyDisplay,
    DateOnly RequestedAtUtc,
    DateOnly? DueByUtc,
    int AgingDays,
    string? AssignedTo);

public sealed record PropertyManagementDashboardMaintenanceAging(
    int Days0To3,
    int Days4To7,
    int Days8To14,
    int Days15Plus);

public sealed record PropertyManagementDashboardPeriods(
    int PendingCloseCount,
    DateOnly? LastClosedPeriod,
    DateOnly? NextClosablePeriod,
    DateOnly? FirstGapPeriod);

public sealed record PropertyManagementDashboardOccupancyPoint(DateOnly Month, int OccupiedUnits, int VacantUnits);

public sealed record PropertyManagementDashboardCollectionsPoint(DateOnly Month, decimal Billed, decimal Collected);
