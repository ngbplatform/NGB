using FluentAssertions;
using NGB.PropertyManagement.Reporting;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Reporting;

public sealed class ReportingReaderDefaultMethodsFullCoverageTests
{
    private static readonly DateOnly Today = new(2026, 8, 22);

    [Fact]
    public async Task DefaultCursorAdapters_ForwardOffsetsAndComputeBothHasMoreOutcomes()
    {
        var maintenanceFake = new MaintenanceReaderFake();
        IMaintenanceQueueReader maintenance = maintenanceFake;
        var maintenanceFirst = await maintenance.GetCursorPageAsync(MaintenanceQuery(99), null);
        var maintenanceLast = await maintenance.GetCursorPageAsync(
            MaintenanceQuery(99), new MaintenanceQueuePageCursor(2, 3));
        maintenanceFake.Offsets.Should().Equal(0, 2);
        maintenanceFirst.HasMore.Should().BeTrue();
        maintenanceLast.HasMore.Should().BeFalse();

        var occupancyFake = new OccupancyReaderFake();
        IOccupancySummaryReader occupancy = occupancyFake;
        var occupancyFirst = await occupancy.GetCursorPageAsync(null, Today, null, 1);
        var occupancyLast = await occupancy.GetCursorPageAsync(
            null, Today, new OccupancySummaryPageCursor(2, 3, OccupancyTotals()), 1);
        occupancyFake.Offsets.Should().Equal(0, 2);
        occupancyFirst.HasMore.Should().BeTrue();
        occupancyLast.HasMore.Should().BeFalse();

        var receivablesFake = new ReceivablesReaderFake();
        IReceivablesReportReader receivables = receivablesFake;
        var receivablesFirst = await receivables.GetCursorPageAsync(
            Guid.NewGuid(), Guid.NewGuid(), ReceivablesReportMode.OpenItemsDetails, null, 1);
        var receivablesLast = await receivables.GetCursorPageAsync(
            Guid.NewGuid(), Guid.NewGuid(), ReceivablesReportMode.Aging,
            new ReceivablesReportPageCursor(2, 3, 0m, 0m, 0m, null, null, null), 1);
        receivablesFake.Offsets.Should().Equal(0, 2);
        receivablesFirst.HasMore.Should().BeTrue();
        receivablesLast.HasMore.Should().BeFalse();

        var tenantFake = new TenantReaderFake();
        ITenantStatementReader tenant = tenantFake;
        var tenantFirst = await tenant.GetCursorPageAsync(TenantQuery(99), null);
        var tenantLast = await tenant.GetCursorPageAsync(
            TenantQuery(99), new TenantStatementPageCursor(2, 3, TenantTotals()));
        tenantFake.Offsets.Should().Equal(0, 2);
        tenantFirst.HasMore.Should().BeTrue();
        tenantLast.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task DefaultMaintenanceDashboard_ClassifiesEveryAgingBucketAndQueueState()
    {
        IMaintenanceQueueReader reader = new MaintenanceReaderFake(
        [
            MaintenanceRow(2, MaintenanceQueueState.Requested),
            MaintenanceRow(4, MaintenanceQueueState.WorkOrdered),
            MaintenanceRow(8, MaintenanceQueueState.Overdue),
            MaintenanceRow(15, MaintenanceQueueState.Overdue)
        ], total: 4);

        var dashboard = await reader.GetDashboardAsync(Today, itemLimit: 4);

        dashboard.Total.Should().Be(4);
        dashboard.Overdue.Should().Be(2);
        dashboard.Days0To3.Should().Be(1);
        dashboard.Days4To7.Should().Be(1);
        dashboard.Days8To14.Should().Be(1);
        dashboard.Days15Plus.Should().Be(1);
        dashboard.Rows.Should().HaveCount(4);
    }

    private static MaintenanceQueueQuery MaintenanceQuery(int offset) =>
        new(Today, null, null, null, null, null, null, offset, 1);

    private static TenantStatementQuery TenantQuery(int offset) =>
        new(Guid.NewGuid(), null, Today, offset, 1);

    private static OccupancySummaryTotals OccupancyTotals() => new(Today, 1, 1, 1);

    private static TenantStatementTotals TenantTotals() => new(null, Today, 0m, 0m, 0m, 0m);

    private static MaintenanceQueueRow MaintenanceRow(int agingDays, MaintenanceQueueState state)
    {
        var hasWorkOrder = state != MaintenanceQueueState.Requested;
        return new MaintenanceQueueRow(
            Guid.NewGuid(), "MR", "Subject", Today.AddDays(-agingDays), agingDays,
            Guid.NewGuid(), "Building", Guid.NewGuid(), "Property", Guid.NewGuid(), "Category",
            "Normal", Guid.NewGuid(), "Tenant",
            hasWorkOrder ? Guid.NewGuid() : null,
            hasWorkOrder ? "WO" : null,
            hasWorkOrder ? Guid.NewGuid() : null,
            hasWorkOrder ? "Vendor" : null,
            hasWorkOrder ? Today.AddDays(1) : null,
            state);
    }

    private sealed class MaintenanceReaderFake(IReadOnlyList<MaintenanceQueueRow>? rows = null, int total = 3)
        : IMaintenanceQueueReader
    {
        private readonly IReadOnlyList<MaintenanceQueueRow> _rows = rows ?? [MaintenanceRow(1, MaintenanceQueueState.Requested)];
        public List<int> Offsets { get; } = [];

        public Task<MaintenanceQueuePage> GetPageAsync(MaintenanceQueueQuery query, CancellationToken ct = default)
        {
            Offsets.Add(query.Offset);
            return Task.FromResult(new MaintenanceQueuePage(_rows, total));
        }
    }

    private sealed class OccupancyReaderFake : IOccupancySummaryReader
    {
        public List<int> Offsets { get; } = [];

        public Task<OccupancySummaryPage> GetPageAsync(
            Guid? buildingId,
            DateOnly asOfUtc,
            int offset,
            int limit,
            CancellationToken ct = default)
        {
            Offsets.Add(offset);
            return Task.FromResult(new OccupancySummaryPage(
                [new OccupancySummaryRow(Guid.NewGuid(), "Building", Today, 1, 1)],
                3,
                OccupancyTotals()));
        }
    }

    private sealed class ReceivablesReaderFake : IReceivablesReportReader
    {
        public List<int> Offsets { get; } = [];

        public Task<ReceivablesReportPage> GetPageAsync(
            Guid registerId,
            Guid leaseId,
            ReceivablesReportMode mode,
            int offset,
            int limit,
            CancellationToken ct = default)
        {
            Offsets.Add(offset);
            return Task.FromResult(new ReceivablesReportPage(
                [new ReceivablesReportRow(true, Guid.NewGuid(), "charge", null, null, null, null, 1m, 1m)],
                3, 1m, 1m, 0m, null, null, null));
        }
    }

    private sealed class TenantReaderFake : ITenantStatementReader
    {
        public List<int> Offsets { get; } = [];

        public Task<TenantStatementPage> GetPageAsync(TenantStatementQuery query, CancellationToken ct = default)
        {
            Offsets.Add(query.Offset);
            return Task.FromResult(new TenantStatementPage(
                [new TenantStatementRow(Today, Guid.NewGuid(), "charge", "Charge", "Charge", null, 1m, 0m, 1m)],
                3,
                TenantTotals()));
        }
    }
}
