using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Reporting;
using NGB.PropertyManagement.Runtime.Dashboard;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Dashboard;

public sealed class PropertyManagementDashboardServiceTests
{
    [Fact]
    public async Task GetAsync_MapsEveryBoundedDashboardSection()
    {
        var asOf = new DateOnly(2026, 8, 23);
        var leaseId = Guid.CreateVersion7();
        var requestId = Guid.CreateVersion7();
        var buildingId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var dashboard = new Mock<IPropertyManagementDashboardReader>(MockBehavior.Strict);
        dashboard.Setup(x => x.GetAsync(asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyManagementDashboardSnapshot(
                new PropertyManagementDashboardPortfolioSnapshot(2, 10, 7, 8),
                new PropertyManagementDashboardLeaseSnapshot(
                    3,
                    2,
                    1,
                    [new PropertyManagementDashboardLeaseEventSnapshot(
                        "Move-in", asOf.AddDays(1), leaseId, "Lease 1", "North")]),
                [new PropertyManagementDashboardOccupancySnapshot(new DateOnly(2026, 8, 1), 7, 3)],
                [new PropertyManagementDashboardCollectionsSnapshot(new DateOnly(2026, 8, 1), 300m, 250m)]));
        var maintenance = new Mock<IMaintenanceQueueReader>(MockBehavior.Strict);
        maintenance.Setup(x => x.GetDashboardAsync(asOf, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MaintenanceQueueDashboard(
                4,
                1,
                1,
                1,
                1,
                1,
                [new MaintenanceQueueRow(
                    requestId,
                    "MR-1",
                    "Repair lift",
                    asOf.AddDays(-5),
                    5,
                    buildingId,
                    "Building",
                    propertyId,
                    "Property",
                    categoryId,
                    "Repair",
                    "High",
                    partyId,
                    "Tenant",
                    null,
                    null,
                    null,
                    null,
                    null,
                    MaintenanceQueueState.Requested)]));
        var reconciliation = new Mock<IReceivablesReconciliationService>(MockBehavior.Strict);
        reconciliation.Setup(x => x.GetAsync(
                It.Is<ReceivablesReconciliationRequest>(request =>
                    request.FromMonthInclusive == new DateOnly(2026, 8, 1)
                    && request.ToMonthInclusive == new DateOnly(2026, 8, 1)
                    && request.Mode == ReceivablesReconciliationMode.Balance
                    && request.Limit == 200),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivablesReconciliationReport(
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 1),
                ReceivablesReconciliationMode.Balance,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                520m,
                500m,
                20m,
                2,
                1,
                [new ReceivablesReconciliationRow(
                    partyId,
                    "Tenant",
                    propertyId,
                    "Property",
                    leaseId,
                    "Lease 1",
                    120m,
                    100m,
                    20m,
                    ReceivablesReconciliationRowKind.Mismatch,
                    true)]));
        var periods = new Mock<IPeriodClosingUiService>(MockBehavior.Strict);
        periods.Setup(x => x.GetCalendarAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PeriodClosingCalendarDto(
                2026,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 1),
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 7, 1),
                true,
                false,
                null,
                [new PeriodCloseStatusDto(
                    new DateOnly(2026, 7, 1), "Open", false, true, null, null,
                    true, false, null, null)]));

        var result = await Create(dashboard, maintenance, reconciliation, periods).GetAsync(asOf);

        result.Warnings.Should().BeEmpty();
        result.Portfolio.Should().BeEquivalentTo(new
        {
            BuildingCount = 2,
            TotalUnits = 10,
            OccupiedUnits = 7,
            VacantUnits = 3,
            OccupancyPercent = 70m,
            FutureOccupiedUnits = 8,
            FutureOccupancyPercent = 80m
        });
        result.Leases.Events.Should().ContainSingle().Which.LeaseId.Should().Be(leaseId);
        result.Receivables.Mismatches.Should().ContainSingle().Which.Diff.Should().Be(20m);
        result.Receivables.CurrentMonthBilled.Should().Be(300m);
        result.Maintenance.Items.Should().ContainSingle().Which.RequestId.Should().Be(requestId);
        result.Periods.PendingCloseCount.Should().Be(1);
        result.OccupancyTrend.Should().ContainSingle();
        result.CollectionsTrend.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAsync_WhenSectionsFail_ReturnsSafeFallbacksAndWarnings()
    {
        var dashboard = new Mock<IPropertyManagementDashboardReader>();
        dashboard.Setup(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("overview"));
        var maintenance = new Mock<IMaintenanceQueueReader>();
        maintenance.Setup(x => x.GetDashboardAsync(It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("maintenance"));
        var reconciliation = new Mock<IReceivablesReconciliationService>();
        reconciliation.Setup(x => x.GetAsync(It.IsAny<ReceivablesReconciliationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("reconciliation"));
        var periods = new Mock<IPeriodClosingUiService>();
        periods.Setup(x => x.GetCalendarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("periods"));

        var result = await Create(dashboard, maintenance, reconciliation, periods)
            .GetAsync(new DateOnly(2026, 8, 23));

        result.Warnings.Should().HaveCount(4);
        result.Portfolio.TotalUnits.Should().Be(0);
        result.Receivables.Mismatches.Should().BeEmpty();
        result.Maintenance.Items.Should().BeEmpty();
        result.OccupancyTrend.Should().BeEmpty();
        result.CollectionsTrend.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var dashboard = new Mock<IPropertyManagementDashboardReader>();
        dashboard.Setup(x => x.GetAsync(It.IsAny<DateOnly>(), cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var service = Create(
            dashboard,
            new Mock<IMaintenanceQueueReader>(),
            new Mock<IReceivablesReconciliationService>(),
            new Mock<IPeriodClosingUiService>());

        var action = () => service.GetAsync(new DateOnly(2026, 8, 23), cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static PropertyManagementDashboardService Create(
        Mock<IPropertyManagementDashboardReader> dashboard,
        Mock<IMaintenanceQueueReader> maintenance,
        Mock<IReceivablesReconciliationService> reconciliation,
        Mock<IPeriodClosingUiService> periods)
        => new(
            dashboard.Object,
            maintenance.Object,
            reconciliation.Object,
            periods.Object,
            NullLogger<PropertyManagementDashboardService>.Instance);
}
