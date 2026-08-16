using FluentAssertions;
using System.Reflection;
using NGB.PropertyManagement.BackgroundJobs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Reporting;

public sealed class PropertyManagementReportingContractsFullCoverageTests
{
    private static readonly DateOnly Today = new(2026, 8, 16);

    [Fact]
    public void Building_summary_calculates_boundaries_and_validates_every_invariant()
    {
        var valid = new BuildingSummary(Guid.NewGuid(), "Building", Today, 3, 1);
        valid.VacantUnits.Should().Be(2);
        valid.VacancyPercent.Should().Be(66.67m);
        AssertAllReadable(valid);
        valid.EnsureInvariant();

        var empty = valid with { TotalUnits = 0, OccupiedUnits = 0 };
        empty.VacancyPercent.Should().Be(0m);
        (valid with { TotalUnits = 1, OccupiedUnits = 2 }).VacantUnits.Should().Be(0);

        AssertInvalid(() => (valid with { BuildingId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { BuildingDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { TotalUnits = -1 }).EnsureInvariant());
        AssertInvalid(() => (valid with { OccupiedUnits = -1 }).EnsureInvariant());
    }

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData(" ", false, 0)]
    [InlineData("requested", true, 1)]
    [InlineData(" work ordered ", true, 2)]
    [InlineData("workorder", true, 2)]
    [InlineData("OPEN", true, 2)]
    [InlineData("overdue", true, 3)]
    [InlineData("unknown", false, 0)]
    public void Maintenance_queue_state_parser_covers_all_aliases_and_invalid_values(
        string? raw,
        bool expectedResult,
        int expectedState)
    {
        MaintenanceQueueStateExtensions.TryParse(raw, out var state).Should().Be(expectedResult);
        ((int)state).Should().Be(expectedState);
    }

    [Fact]
    public void Maintenance_queue_query_validates_offset_and_limit_boundaries()
    {
        var valid = new MaintenanceQueueQuery(Today, null, null, null, null, null, null, 0, 1);
        AssertAllReadable(valid);
        valid.EnsureInvariant();

        ((Action)(() => (valid with { Offset = -1 }).EnsureInvariant()))
            .Should().Throw<NgbArgumentOutOfRangeException>();
        ((Action)(() => (valid with { Limit = 0 }).EnsureInvariant()))
            .Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public void Maintenance_queue_row_accepts_requested_and_work_ordered_shapes()
    {
        var requested = CreateMaintenanceRow();
        AssertAllReadable(requested);
        requested.EnsureInvariant();

        var workOrdered = requested with
        {
            WorkOrderId = Guid.NewGuid(),
            WorkOrderDisplay = "WO-1",
            AssignedPartyId = Guid.NewGuid(),
            AssignedPartyDisplay = "Vendor",
            DueByUtc = Today.AddDays(1),
            QueueState = MaintenanceQueueState.WorkOrdered
        };
        workOrdered.EnsureInvariant();
    }

    [Fact]
    public void Maintenance_queue_row_validates_identifiers_text_and_aging()
    {
        var valid = CreateMaintenanceRow();

        AssertInvalid(() => (valid with { RequestId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { RequestDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { Subject = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { AgingDays = -1 }).EnsureInvariant());
        AssertInvalid(() => (valid with { BuildingId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { PropertyId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { CategoryId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { RequestedByPartyId = Guid.Empty }).EnsureInvariant());

        AssertInvalid(() => (valid with { BuildingDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { PropertyDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { CategoryDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { RequestedByDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { Priority = " " }).EnsureInvariant());
    }

    [Fact]
    public void Maintenance_queue_row_validates_requested_and_work_order_detail_consistency()
    {
        var requested = CreateMaintenanceRow();
        AssertInvalid(() => (requested with { QueueState = MaintenanceQueueState.Overdue }).EnsureInvariant());
        AssertInvalid(() => (requested with { WorkOrderDisplay = "WO" }).EnsureInvariant());
        AssertInvalid(() => (requested with { AssignedPartyId = Guid.NewGuid() }).EnsureInvariant());
        AssertInvalid(() => (requested with { AssignedPartyDisplay = "Vendor" }).EnsureInvariant());
        AssertInvalid(() => (requested with { DueByUtc = Today }).EnsureInvariant());

        var workOrdered = requested with
        {
            WorkOrderId = Guid.NewGuid(),
            QueueState = MaintenanceQueueState.WorkOrdered
        };
        AssertInvalid(() => workOrdered.EnsureInvariant());
    }

    [Fact]
    public void Maintenance_queue_page_validates_collection_total_and_nested_rows()
    {
        ((Action)(() => new MaintenanceQueuePage(null!, 0).EnsureInvariant()))
            .Should().Throw<NgbArgumentRequiredException>();
        AssertInvalid(() => new MaintenanceQueuePage([], -1).EnsureInvariant());

        new MaintenanceQueuePage([], 0).EnsureInvariant();
        var page = new MaintenanceQueuePage([CreateMaintenanceRow()], 1);
        AssertAllReadable(page);
        page.EnsureInvariant();
        AssertInvalid(() => new MaintenanceQueuePage(
            [CreateMaintenanceRow() with { RequestId = Guid.Empty }], 1).EnsureInvariant());
    }

    [Fact]
    public void Occupancy_row_calculates_boundaries_and_validates_every_invariant()
    {
        var valid = new OccupancySummaryRow(Guid.NewGuid(), "Building", Today, 3, 2);
        valid.VacantUnits.Should().Be(1);
        valid.OccupancyPercent.Should().Be(66.67m);
        AssertAllReadable(valid);
        valid.EnsureInvariant();

        (valid with { TotalUnits = 0, OccupiedUnits = 0 }).OccupancyPercent.Should().Be(0m);
        (valid with { TotalUnits = 1, OccupiedUnits = 2 }).VacantUnits.Should().Be(0);

        AssertInvalid(() => (valid with { BuildingId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { BuildingDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { TotalUnits = -1 }).EnsureInvariant());
        AssertInvalid(() => (valid with { OccupiedUnits = -1 }).EnsureInvariant());
        AssertInvalid(() => (valid with { OccupiedUnits = 4 }).EnsureInvariant());
    }

    [Fact]
    public void Occupancy_totals_calculate_boundaries_and_validate_every_count()
    {
        var valid = new OccupancySummaryTotals(Today, 1, 3, 2);
        valid.VacantUnits.Should().Be(1);
        valid.OccupancyPercent.Should().Be(66.67m);
        AssertAllReadable(valid);
        valid.EnsureInvariant();

        (valid with { TotalUnits = 0, OccupiedUnits = 0 }).OccupancyPercent.Should().Be(0m);
        (valid with { TotalUnits = 1, OccupiedUnits = 2 }).VacantUnits.Should().Be(0);

        AssertInvalid(() => (valid with { BuildingCount = -1 }).EnsureInvariant());
        AssertInvalid(() => (valid with { TotalUnits = -1 }).EnsureInvariant());
        AssertInvalid(() => (valid with { OccupiedUnits = -1 }).EnsureInvariant());
        AssertInvalid(() => (valid with { OccupiedUnits = 4 }).EnsureInvariant());
    }

    [Fact]
    public void Occupancy_page_validates_collection_total_totals_and_nested_rows()
    {
        var totals = new OccupancySummaryTotals(Today, 1, 1, 1);
        var row = new OccupancySummaryRow(Guid.NewGuid(), "Building", Today, 1, 1);

        ((Action)(() => new OccupancySummaryPage(null!, 0, totals).EnsureInvariant()))
            .Should().Throw<NgbArgumentRequiredException>();
        AssertInvalid(() => new OccupancySummaryPage([], -1, totals).EnsureInvariant());
        AssertInvalid(() => new OccupancySummaryPage([], 0, totals with { BuildingCount = -1 }).EnsureInvariant());
        AssertInvalid(() => new OccupancySummaryPage([row with { BuildingId = Guid.Empty }], 1, totals).EnsureInvariant());
        new OccupancySummaryPage([], 0, totals).EnsureInvariant();
        var page = new OccupancySummaryPage([row], 1, totals);
        AssertAllReadable(page);
        page.EnsureInvariant();
    }

    [Fact]
    public void Tenant_statement_query_validates_all_date_and_paging_boundaries()
    {
        var valid = new TenantStatementQuery(Guid.NewGuid(), null, Today, 0, 1);
        AssertAllReadable(valid);
        valid.EnsureInvariant();
        (valid with { FromUtc = Today }).EnsureInvariant();

        AssertInvalid(() => (valid with { LeaseId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { FromUtc = Today.AddDays(1) }).EnsureInvariant());
        ((Action)(() => (valid with { Offset = -1 }).EnsureInvariant()))
            .Should().Throw<NgbArgumentOutOfRangeException>();
        ((Action)(() => (valid with { Limit = 0 }).EnsureInvariant()))
            .Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public void Tenant_statement_row_accepts_charge_credit_and_zero_shapes()
    {
        CreateStatementRow(charge: 10m, credit: 0m).EnsureInvariant();
        CreateStatementRow(charge: 0m, credit: 10m).EnsureInvariant();
        CreateStatementRow(charge: 0m, credit: 0m).EnsureInvariant();
    }

    [Fact]
    public void Tenant_statement_row_validates_every_identifier_text_and_amount_rule()
    {
        var valid = CreateStatementRow(10m, 0m);
        AssertAllReadable(valid);

        AssertInvalid(() => (valid with { DocumentId = Guid.Empty }).EnsureInvariant());
        AssertInvalid(() => (valid with { DocumentType = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { DocumentDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { EntryTypeDisplay = " " }).EnsureInvariant());
        AssertInvalid(() => (valid with { ChargeAmount = -1m }).EnsureInvariant());
        AssertInvalid(() => (valid with { CreditAmount = -1m }).EnsureInvariant());
        AssertInvalid(() => (valid with { ChargeAmount = 1m, CreditAmount = 1m }).EnsureInvariant());
    }

    [Fact]
    public void Tenant_statement_totals_validate_amounts_and_expose_date_boundaries()
    {
        var valid = new TenantStatementTotals(null, Today, 1m, 2m, 1m, 2m);
        valid.FromUtc.Should().BeNull();
        valid.ToUtc.Should().Be(Today);
        AssertAllReadable(valid);
        valid.EnsureInvariant();

        AssertInvalid(() => (valid with { TotalCharges = -1m }).EnsureInvariant());
        AssertInvalid(() => (valid with { TotalCredits = -1m }).EnsureInvariant());
    }

    [Fact]
    public void Tenant_statement_page_validates_collection_total_totals_and_nested_rows()
    {
        var totals = new TenantStatementTotals(null, Today, 0m, 0m, 0m, 0m);
        var row = CreateStatementRow(0m, 0m);

        ((Action)(() => new TenantStatementPage(null!, 0, totals).EnsureInvariant()))
            .Should().Throw<NgbArgumentRequiredException>();
        AssertInvalid(() => new TenantStatementPage([], -1, totals).EnsureInvariant());
        AssertInvalid(() => new TenantStatementPage([], 0, totals with { TotalCharges = -1m }).EnsureInvariant());
        AssertInvalid(() => new TenantStatementPage([row with { DocumentId = Guid.Empty }], 1, totals).EnsureInvariant());
        new TenantStatementPage([], 0, totals).EnsureInvariant();
        var page = new TenantStatementPage([row], 1, totals);
        AssertAllReadable(page);
        page.EnsureInvariant();
    }

    [Fact]
    public void Infrastructure_contract_records_expose_previously_unread_properties()
    {
        var id = Guid.NewGuid();
        var period = new PmRentChargePeriodKey(id, Today, Today.AddMonths(1).AddDays(-1));
        period.LeaseId.Should().Be(id);
        period.PeriodFromUtc.Should().Be(Today);

        var lease = new PmLeaseHead(id, Guid.NewGuid(), Guid.NewGuid(), Today, null);
        lease.LeaseId.Should().Be(id);

        var request = new PmMaintenanceRequestHead(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "high", "Subject", null, Today);
        request.DocumentId.Should().Be(id);
        request.Description.Should().BeNull();

        var workOrder = new PmWorkOrderHead(id, Guid.NewGuid(), null, null, null, "owner");
        workOrder.DocumentId.Should().Be(id);
        workOrder.ScopeOfWork.Should().BeNull();
        workOrder.DueByUtc.Should().BeNull();

        var completion = new PmWorkOrderCompletionHead(id, Guid.NewGuid(), Today, "done", null);
        completion.DocumentId.Should().Be(id);
        completion.ClosedAtUtc.Should().Be(Today);

        new PmRentChargeHead(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today, Today, Today, 1m, null)
            .DocumentId.Should().Be(id);
        new PmLateFeeChargeHead(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today, 1m, null)
            .DocumentId.Should().Be(id);
        new PmReceivableReturnedPaymentHead(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Today, 1m, null)
            .DocumentId.Should().Be(id);
        new PmPayableApplyHead(id, Guid.NewGuid(), Guid.NewGuid(), Today, 1m, null)
            .DocumentId.Should().Be(id);
        new PmReceivableApplyHead(id, Guid.NewGuid(), Guid.NewGuid(), Today, 1m, null)
            .DocumentId.Should().Be(id);
    }

    private static MaintenanceQueueRow CreateMaintenanceRow()
        => new(
            RequestId: Guid.NewGuid(),
            RequestDisplay: "MR-1",
            Subject: "Leaking faucet",
            RequestedAtUtc: Today,
            AgingDays: 0,
            BuildingId: Guid.NewGuid(),
            BuildingDisplay: "Building",
            PropertyId: Guid.NewGuid(),
            PropertyDisplay: "Unit 1",
            CategoryId: Guid.NewGuid(),
            CategoryDisplay: "Plumbing",
            Priority: "normal",
            RequestedByPartyId: Guid.NewGuid(),
            RequestedByDisplay: "Tenant",
            WorkOrderId: null,
            WorkOrderDisplay: null,
            AssignedPartyId: null,
            AssignedPartyDisplay: null,
            DueByUtc: null,
            QueueState: MaintenanceQueueState.Requested);

    private static TenantStatementRow CreateStatementRow(decimal charge, decimal credit)
        => new(
            OccurredOnUtc: Today,
            DocumentId: Guid.NewGuid(),
            DocumentType: "pm.rent_charge",
            DocumentDisplay: "RC-1",
            EntryTypeDisplay: "Rent charge",
            Description: null,
            ChargeAmount: charge,
            CreditAmount: credit,
            RunningBalance: charge - credit);

    private static void AssertInvalid(Action action) => action.Should().Throw<NgbArgumentInvalidException>();

    private static void AssertAllReadable(params object[] values)
    {
        foreach (var value in values)
        {
            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length == 0)
                    property.GetValue(value);
            }
        }
    }
}
