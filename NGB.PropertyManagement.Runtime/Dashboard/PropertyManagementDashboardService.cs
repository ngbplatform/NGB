using Microsoft.Extensions.Logging;
using NGB.Application.Abstractions.Services;
using NGB.PropertyManagement.Contracts.Dashboard;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Reporting;

namespace NGB.PropertyManagement.Runtime.Dashboard;

public sealed class PropertyManagementDashboardService(
    IPropertyManagementDashboardReader dashboardReader,
    IMaintenanceQueueReader maintenanceReader,
    IReceivablesReconciliationService reconciliationService,
    IPeriodClosingUiService periodClosingService,
    ILogger<PropertyManagementDashboardService> logger)
    : IPropertyManagementDashboardService
{
    private const int MaintenanceItemLimit = 6;
    private const int ReconciliationPreviewLimit = 200;

    public async Task<PropertyManagementDashboardResponse> GetAsync(DateOnly asOfUtc, CancellationToken ct = default)
    {
        var warnings = new List<string>();

        var overview = await CaptureAsync(
            "Portfolio analytics are unavailable",
            token => dashboardReader.GetAsync(asOfUtc, token),
            warnings,
            ct);
        var maintenance = await CaptureAsync(
            "Maintenance queue is unavailable",
            token => maintenanceReader.GetDashboardAsync(asOfUtc, MaintenanceItemLimit, token),
            warnings,
            ct);

        var month = new DateOnly(asOfUtc.Year, asOfUtc.Month, 1);
        var reconciliation = await CaptureAsync(
            "Receivables reconciliation is unavailable",
            token => reconciliationService.GetAsync(
                new ReceivablesReconciliationRequest(
                    month,
                    month,
                    ReceivablesReconciliationMode.Balance,
                    Offset: 0,
                    Limit: ReconciliationPreviewLimit),
                token),
            warnings,
            ct);
        var calendar = await CaptureAsync(
            "Period closing status is unavailable",
            token => periodClosingService.GetCalendarAsync(asOfUtc.Year, token),
            warnings,
            ct);

        var portfolio = overview?.Portfolio;
        var totalUnits = portfolio?.TotalUnits ?? 0;
        var occupiedUnits = portfolio?.OccupiedUnits ?? 0;
        var futureOccupiedUnits = portfolio?.FutureOccupiedUnits ?? 0;
        var collectionTrend = overview?.CollectionsTrend ?? [];
        var currentCollection = collectionTrend.LastOrDefault();

        return new PropertyManagementDashboardResponse(
            asOfUtc,
            warnings,
            new PropertyManagementDashboardPortfolio(
                portfolio?.BuildingCount ?? 0,
                totalUnits,
                occupiedUnits,
                Math.Max(0, totalUnits - occupiedUnits),
                Percent(occupiedUnits, totalUnits),
                futureOccupiedUnits,
                Percent(futureOccupiedUnits, totalUnits)),
            new PropertyManagementDashboardLeases(
                overview?.Leases.Expiring30Count ?? 0,
                overview?.Leases.UpcomingMoveInCount ?? 0,
                overview?.Leases.UpcomingMoveOutCount ?? 0,
                overview?.Leases.Events.Select(static item =>
                    new PropertyManagementDashboardLeaseEvent(
                        item.Kind,
                        item.Date,
                        item.LeaseId,
                        item.LeaseDisplay,
                        item.PropertyDisplay)).ToArray() ?? []),
            new PropertyManagementDashboardReceivables(
                reconciliation?.TotalOpenItemsNet ?? 0,
                reconciliation?.TotalDiff ?? 0,
                reconciliation?.RowCount ?? 0,
                reconciliation?.MismatchRowCount ?? 0,
                reconciliation?.Rows
                    .Where(static row => row.HasDiff)
                    .OrderByDescending(static row => Math.Abs(row.Diff))
                    .Take(6)
                    .Select(static row => new PropertyManagementDashboardMismatch(
                        row.PartyId,
                        row.PropertyId,
                        row.LeaseId,
                        row.LeaseDisplay ?? row.LeaseId.ToString(),
                        row.PropertyDisplay ?? row.PropertyId.ToString(),
                        row.RowKind.ToString(),
                        row.Diff))
                    .ToArray() ?? [],
                currentCollection?.Billed ?? 0,
                currentCollection?.Collected ?? 0),
            new PropertyManagementDashboardMaintenance(
                maintenance?.Total ?? 0,
                maintenance?.Overdue ?? 0,
                maintenance?.Rows.Select(static row =>
                    new PropertyManagementDashboardMaintenanceItem(
                        row.RequestId,
                        row.WorkOrderId,
                        row.QueueState.ToString(),
                        row.Subject,
                        row.RequestDisplay,
                        row.PropertyDisplay,
                        row.RequestedAtUtc,
                        row.DueByUtc,
                        row.AgingDays,
                        row.AssignedPartyDisplay)).ToArray() ?? [],
                new PropertyManagementDashboardMaintenanceAging(
                    maintenance?.Days0To3 ?? 0,
                    maintenance?.Days4To7 ?? 0,
                    maintenance?.Days8To14 ?? 0,
                    maintenance?.Days15Plus ?? 0)),
            new PropertyManagementDashboardPeriods(
                calendar?.Months.Count(static item => item.HasActivity && !item.IsClosed) ?? 0,
                calendar?.LatestContiguousClosedPeriod ?? calendar?.LatestClosedPeriod,
                calendar?.NextClosablePeriod,
                calendar?.FirstGapPeriod),
            overview?.OccupancyTrend.Select(static item =>
                new PropertyManagementDashboardOccupancyPoint(
                    item.Month,
                    item.OccupiedUnits,
                    item.VacantUnits)).ToArray() ?? [],
            collectionTrend.Select(static item =>
                new PropertyManagementDashboardCollectionsPoint(
                    item.Month,
                    item.Billed,
                    item.Collected)).ToArray());
    }

    private async Task<T?> CaptureAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> factory,
        ICollection<string> warnings,
        CancellationToken ct)
        where T : class
    {
        try
        {
            return await factory(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{DashboardSection}.", label);
            warnings.Add(label + ".");
            return null;
        }
    }

    private static decimal Percent(int value, int total)
        => total > 0 ? decimal.Round(value * 100m / total, 2) : 0m;
}
