using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Reporting.Exceptions;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Reporting;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Reporting;

public sealed class PropertyManagementCanonicalExecutorsFullCoverageTests
{
    private static readonly DateOnly Today = new(2026, 8, 16);

    [Fact]
    public void Definition_source_exposes_complete_canonical_contracts()
    {
        var definitions = new PropertyManagementCanonicalReportDefinitionSource().GetDefinitions();

        definitions.Select(x => x.ReportCode).Should().BeEquivalentTo(
            PropertyManagementSecurityDefaults.BuildingSummaryReport,
            PropertyManagementSecurityDefaults.OccupancySummaryReport,
            PropertyManagementSecurityDefaults.MaintenanceQueueReport,
            PropertyManagementSecurityDefaults.TenantStatementReport,
            PropertyManagementSecurityDefaults.ReceivablesAgingReport,
            PropertyManagementSecurityDefaults.ReceivablesOpenItemsReport,
            PropertyManagementSecurityDefaults.ReceivablesOpenItemsDetailsReport);
        definitions.Should().OnlyContain(x => x.Mode == ReportExecutionMode.Canonical);
        definitions.Single(x => x.ReportCode == PropertyManagementSecurityDefaults.MaintenanceQueueReport)
            .Filters.Should().HaveCount(6);
    }

    [Fact]
    public async Task Building_summary_covers_explicit_and_default_date_totals_and_invalid_reader_result()
    {
        var buildingId = Guid.CreateVersion7();
        var reader = new Mock<IBuildingSummaryReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetSummaryAsync(buildingId, Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildingSummary(buildingId, "North", Today, 4, 3));
        reader.Setup(x => x.GetSummaryAsync(buildingId, It.Is<DateOnly>(d => d != Today), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildingSummary(buildingId, "North", Today, 4, 3));
        var sut = new BuildingSummaryCanonicalReportExecutor(reader.Object);
        var definition = Definition(sut.ReportCode);

        var withTotals = await sut.ExecuteAsync(definition, Request(
            filters: Filters(("building_id", Json(buildingId))),
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") },
            limit: 7), default);
        var withoutTotals = await sut.ExecuteAsync(definition, Request(
            filters: Filters(("building_id", Json(buildingId))),
            layout: new ReportLayoutDto(ShowGrandTotals: false)), default);

        sut.ReportCode.Should().Be(PropertyManagementSecurityDefaults.BuildingSummaryReport);
        withTotals.PrebuiltSheet!.Rows.Should().HaveCount(2);
        withTotals.Limit.Should().Be(7);
        withoutTotals.PrebuiltSheet!.Rows.Should().ContainSingle();
        withTotals.PrebuiltSheet.Rows[0].Cells[0].Action.Should().NotBeNull();

        reader.Setup(x => x.GetSummaryAsync(buildingId, Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildingSummary(Guid.Empty, "Invalid", Today, 1, 1));
        var invalid = () => sut.ExecuteAsync(definition, Request(
            filters: Filters(("building_id", Json(buildingId))),
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") }), default);
        await invalid.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Occupancy_summary_covers_portfolio_building_paging_totals_and_empty_page()
    {
        var buildingId = Guid.CreateVersion7();
        var row = new OccupancySummaryRow(buildingId, "North", Today, 4, 3);
        var totals = new OccupancySummaryTotals(Today, 1, 4, 3);
        var reader = new Mock<IOccupancySummaryReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetPageAsync(null, Today, 0, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OccupancySummaryPage([row], 2, totals, HasMore: true));
        reader.Setup(x => x.GetCursorPageAsync(
                null, Today, It.Is<OccupancySummaryPageCursor>(cursor => cursor.Offset == 1), 1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OccupancySummaryPage([row], 2, totals, HasMore: false));
        reader.Setup(x => x.GetPageAsync(buildingId, Today, 2, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OccupancySummaryPage([row], 3, totals));
        reader.Setup(x => x.GetPageAsync(
                null, Today, 0, It.Is<int>(limit => limit > 200), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OccupancySummaryPage([], 0, new OccupancySummaryTotals(Today, 0, 0, 0)));
        reader.Setup(x => x.GetPageAsync(null, It.IsAny<DateOnly>(), 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OccupancySummaryPage([], 0, new OccupancySummaryTotals(Today, 0, 0, 0)));
        var sut = new OccupancySummaryCanonicalReportExecutor(reader.Object);
        var definition = Definition(sut.ReportCode);

        var portfolio = await sut.ExecuteAsync(definition, Request(
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") },
            offset: -4,
            limit: 1), default);
        var building = await sut.ExecuteAsync(definition, Request(
            filters: Filters(("building_id", Json(buildingId))),
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") },
            layout: new ReportLayoutDto(ShowGrandTotals: false),
            offset: 2,
            limit: 0), default);
        var cursorPage = await sut.ExecuteAsync(definition, Request(
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") },
            limit: 1,
            cursor: portfolio.NextCursor), default);
        var unpaged = await sut.ExecuteAsync(definition, Request(
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") },
            disablePaging: true), default);
        var empty = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(), default);

        sut.ReportCode.Should().Be(PropertyManagementSecurityDefaults.OccupancySummaryReport);
        portfolio.HasMore.Should().BeTrue();
        portfolio.PrebuiltSheet.Should().NotBeNull();
        var portfolioSheet = portfolio.PrebuiltSheet!;
        portfolioSheet.Rows.Should().HaveCount(2);
        portfolioSheet.Meta!.Subtitle!.Should().StartWith("Portfolio occupancy");
        building.HasMore.Should().BeFalse();
        building.PrebuiltSheet.Should().NotBeNull();
        var buildingSheet = building.PrebuiltSheet!;
        buildingSheet.Rows.Should().ContainSingle();
        buildingSheet.Meta!.Subtitle!.Should().StartWith("Occupancy");
        cursorPage.Offset.Should().Be(1);
        cursorPage.HasMore.Should().BeFalse();
        unpaged.Limit.Should().BeGreaterThan(200);
        unpaged.NextCursor.Should().BeNull();
        empty.PrebuiltSheet!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Maintenance_queue_maps_rows_all_filters_and_paging_boundaries()
    {
        var requested = MaintenanceRow();
        var assigned = requested with
        {
            WorkOrderId = Guid.CreateVersion7(),
            WorkOrderDisplay = "WO-1",
            AssignedPartyId = Guid.CreateVersion7(),
            AssignedPartyDisplay = "Vendor",
            DueByUtc = Today.AddDays(2),
            QueueState = MaintenanceQueueState.WorkOrdered
        };
        var reader = new Mock<IMaintenanceQueueReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetPageAsync(It.IsAny<MaintenanceQueueQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MaintenanceQueueQuery query, CancellationToken _) =>
                new MaintenanceQueuePage(
                    [requested, assigned],
                    query.Offset == 0 ? 3 : 2,
                    HasMore: query.Offset == 0));
        reader.Setup(x => x.GetCursorPageAsync(
                It.Is<MaintenanceQueueQuery>(query => query.Offset == 2),
                It.Is<MaintenanceQueuePageCursor>(cursor => cursor.Offset == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MaintenanceQueuePage([], 2));
        var sut = new MaintenanceQueueCanonicalReportExecutor(reader.Object);
        var definition = Definition(sut.ReportCode);
        var buildingId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();

        var page = await sut.ExecuteAsync(definition, Request(
            filters: Filters(
                ("building_id", Json(buildingId)),
                ("property_id", Json(propertyId)),
                ("category_id", Json(categoryId)),
                ("assigned_party_id", Json(partyId)),
                ("priority", Json(" emergency ")),
                ("queue_state", Json(" work ordered "))),
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") },
            offset: -1,
            limit: 0), default);
        var noFilters = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(Offset: 1, Limit: 5), default);
        var cursorPage = await sut.ExecuteAsync(definition, Request(
            filters: Filters(
                ("building_id", Json(buildingId)),
                ("property_id", Json(propertyId)),
                ("category_id", Json(categoryId)),
                ("assigned_party_id", Json(partyId)),
                ("priority", Json(" emergency ")),
                ("queue_state", Json(" work ordered "))),
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") },
            cursor: page.NextCursor), default);
        var unpaged = await sut.ExecuteAsync(definition, Request(disablePaging: true), default);

        sut.ReportCode.Should().Be(PropertyManagementCodes.MaintenanceQueue);
        page.Offset.Should().Be(0);
        page.Limit.Should().Be(100);
        page.HasMore.Should().BeTrue();
        page.PrebuiltSheet!.Rows.Should().HaveCount(2);
        page.PrebuiltSheet.Rows[0].Cells[10].Action.Should().BeNull();
        page.PrebuiltSheet.Rows[1].Cells[10].Action.Should().NotBeNull();
        page.PrebuiltSheet.Rows[0].Cells[11].Action.Should().BeNull();
        page.PrebuiltSheet.Rows[1].Cells[11].Action.Should().NotBeNull();
        noFilters.HasMore.Should().BeFalse();
        cursorPage.Offset.Should().Be(2);
        cursorPage.HasMore.Should().BeFalse();
        unpaged.Limit.Should().BeGreaterThan(200);
        unpaged.NextCursor.Should().BeNull();

        reader.Verify(x => x.GetPageAsync(It.Is<MaintenanceQueueQuery>(q =>
            q.BuildingId == buildingId && q.PropertyId == propertyId && q.CategoryId == categoryId &&
            q.AssignedPartyId == partyId && q.Priority == "Emergency" &&
            q.QueueState == MaintenanceQueueState.WorkOrdered && q.Offset == 0 && q.Limit == 100),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("HIGH", "High")]
    [InlineData("normal", "Normal")]
    [InlineData("Low", "Low")]
    public async Task Maintenance_queue_normalizes_each_supported_priority(string raw, string expected)
    {
        var captured = default(MaintenanceQueueQuery);
        var reader = MaintenanceReader(query => captured = query);
        var sut = new MaintenanceQueueCanonicalReportExecutor(reader.Object);

        await sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: Filters(("priority", Json(raw)))), default);

        captured!.Priority.Should().Be(expected);
    }

    [Fact]
    public async Task Maintenance_queue_treats_null_blank_undefined_and_unrelated_text_filters_as_absent()
    {
        var reader = MaintenanceReader();
        var sut = new MaintenanceQueueCanonicalReportExecutor(reader.Object);
        var definition = Definition(sut.ReportCode);

        await sut.ExecuteAsync(definition, Request(filters: Filters(("unrelated", Json("x")))), default);
        await sut.ExecuteAsync(definition, Request(filters: Filters(("priority", Json((string?)null)))), default);
        await sut.ExecuteAsync(definition, Request(filters: Filters(("priority", Json("   ")))), default);
        await sut.ExecuteAsync(definition, Request(filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["priority"] = new(default)
        }), default);

        reader.Verify(x => x.GetPageAsync(It.Is<MaintenanceQueueQuery>(q => q.Priority == null && q.QueueState == null),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Theory]
    [InlineData("priority", "urgent")]
    [InlineData("queue_state", "closed")]
    public async Task Maintenance_queue_rejects_unknown_text_filter_values(string filter, string value)
    {
        var sut = new MaintenanceQueueCanonicalReportExecutor(MaintenanceReader().Object);
        var act = () => sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: Filters((filter, Json(value)))), default);

        await act.Should().ThrowAsync<ReportLayoutValidationException>();
    }

    [Theory]
    [InlineData("priority")]
    [InlineData("queue_state")]
    public async Task Maintenance_queue_rejects_non_text_filters(string filter)
    {
        var sut = new MaintenanceQueueCanonicalReportExecutor(MaintenanceReader().Object);
        var act = () => sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: Filters((filter, Json(42)))), default);

        await act.Should().ThrowAsync<ReportLayoutValidationException>();
    }

    [Fact]
    public async Task Receivables_open_items_covers_both_kinds_null_displays_actions_totals_and_paging()
    {
        var chargeId = Guid.CreateVersion7();
        var registerId = Guid.CreateVersion7();
        ReceivablesOpenItemPageRow[] responseRows =
        [
            new(true, chargeId, "Charge", 12m, PropertyManagementCodes.ReceivableCharge),
            new(true, Guid.CreateVersion7(), null, 3m, " "),
            new(false, Guid.CreateVersion7(), "Credit", 4m, PropertyManagementCodes.ReceivablePayment)
        ];
        var response = new ReceivablesOpenItemsResponse(registerId,
            [
                new ReceivablesOpenItemDto(chargeId, "Charge", 12m, PropertyManagementCodes.ReceivableCharge),
                new ReceivablesOpenItemDto(Guid.CreateVersion7(), null, 3m, " ")
            ],
            [new ReceivablesOpenItemDto(Guid.CreateVersion7(), "Credit", 4m, PropertyManagementCodes.ReceivablePayment)],
            15m,
            4m);
        var service = new Mock<IReceivablesOpenItemsService>(MockBehavior.Strict);
        service.Setup(x => x.GetOpenItemsCursorPageAsync(
                Guid.Empty, Guid.Empty, It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, string? _, int limit, CancellationToken _) =>
                new ReceivablesOpenItemsPageResponse(
                    registerId,
                    responseRows.Take(limit).ToArray(),
                    responseRows.Length,
                    response.TotalOutstanding,
                    response.TotalCredit,
                    HasMore: limit < responseRows.Length,
                    NextCursor: limit < responseRows.Length ? "next" : null));
        service.Setup(x => x.GetOpenItemsPageAsync(
                Guid.Empty, Guid.Empty, It.IsAny<Guid>(), 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivablesOpenItemsPageResponse(
                registerId,
                responseRows.Skip(1).Take(1).ToArray(),
                responseRows.Length,
                response.TotalOutstanding,
                response.TotalCredit,
                Offset: 1));
        var sut = new ReceivablesOpenItemsCanonicalReportExecutor(service.Object);
        var leaseId = Guid.CreateVersion7();
        var definition = Definition(sut.ReportCode);

        var first = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), offset: -1, limit: 1), default);
        var allWithoutTotals = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), layout: new ReportLayoutDto(ShowGrandTotals: false), limit: 0), default);
        var legacyOffset = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), offset: 1, limit: 1), default);

        sut.ReportCode.Should().Be(PropertyManagementSecurityDefaults.ReceivablesOpenItemsReport);
        first.Total.Should().Be(3);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().Be("next");
        first.PrebuiltSheet!.Rows.Should().HaveCount(2);
        first.PrebuiltSheet.Rows[0].Cells[1].Action.Should().NotBeNull();
        allWithoutTotals.PrebuiltSheet!.Rows.Should().HaveCount(3);
        allWithoutTotals.PrebuiltSheet.Rows[1].Cells[1].Display.Should().BeEmpty();
        allWithoutTotals.PrebuiltSheet.Rows[1].Cells[1].Action.Should().BeNull();
        legacyOffset.Offset.Should().Be(1);
        legacyOffset.HasMore.Should().BeTrue();
        legacyOffset.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Receivables_open_items_omits_totals_for_empty_response()
    {
        var service = new Mock<IReceivablesOpenItemsService>(MockBehavior.Strict);
        service.Setup(x => x.GetOpenItemsCursorPageAsync(
                Guid.Empty, Guid.Empty, It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivablesOpenItemsPageResponse(Guid.CreateVersion7(), [], 0, 0m, 0m));
        var sut = new ReceivablesOpenItemsCanonicalReportExecutor(service.Object);

        var page = await sut.ExecuteAsync(Definition(sut.ReportCode), Request(filters: LeaseFilter(Guid.CreateVersion7())), default);

        page.HasMore.Should().BeFalse();
        page.PrebuiltSheet!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Receivables_open_item_details_covers_charge_credit_nulls_actions_totals_and_paging()
    {
        var response = DetailsResponse(
            charges:
            [
                new(Guid.CreateVersion7(), PropertyManagementCodes.ReceivableCharge, "RC-1", "Charge", Today,
                    Guid.CreateVersion7(), "Rent", "Memo", 12m, 10m),
                new(Guid.CreateVersion7(), " ", null, null, Today.AddDays(1), null, null, null, 3m, 2m)
            ],
            credits:
            [new(Guid.CreateVersion7(), PropertyManagementCodes.ReceivablePayment, "RP-1", "Credit", Today, null, 5m, 4m)]);
        var reportReader = ReceivablesReportReader(response);
        var sut = new ReceivablesOpenItemsDetailsCanonicalReportExecutor(reportReader.Object, PolicyReader());
        var leaseId = Guid.CreateVersion7();
        var definition = Definition(sut.ReportCode);

        var first = await sut.ExecuteAsync(definition, Request(filters: LeaseFilter(leaseId), offset: -2, limit: 1), default);
        var cursorPage = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), limit: 1, cursor: first.NextCursor), default);
        var all = await sut.ExecuteAsync(definition, Request(filters: LeaseFilter(leaseId), limit: 0), default);
        var unpaged = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), disablePaging: true), default);
        var withoutTotals = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), layout: new ReportLayoutDto(ShowGrandTotals: false)), default);

        sut.ReportCode.Should().Be(PropertyManagementSecurityDefaults.ReceivablesOpenItemsDetailsReport);
        first.HasMore.Should().BeTrue();
        first.PrebuiltSheet!.Rows.Should().HaveCount(2);
        all.PrebuiltSheet!.Rows.Should().HaveCount(4);
        all.PrebuiltSheet.Rows[1].Cells[1].Display.Should().BeEmpty();
        all.PrebuiltSheet.Rows[1].Cells[1].Action.Should().BeNull();
        withoutTotals.PrebuiltSheet!.Rows.Should().HaveCount(3);
        cursorPage.Offset.Should().Be(1);
        unpaged.Limit.Should().BeGreaterThan(200);
        unpaged.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Receivables_open_item_details_omits_totals_for_empty_response()
    {
        var sut = new ReceivablesOpenItemsDetailsCanonicalReportExecutor(
            ReceivablesReportReader(DetailsResponse([], [])).Object,
            PolicyReader());

        var page = await sut.ExecuteAsync(Definition(sut.ReportCode), Request(filters: LeaseFilter(Guid.CreateVersion7())), default);

        page.PrebuiltSheet!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Receivables_aging_covers_every_bucket_null_displays_actions_totals_and_paging()
    {
        var asOf = Today;
        var charges = new[]
        {
            AgingCharge(asOf.AddDays(1), "Current", PropertyManagementCodes.ReceivableCharge, "Rent"),
            AgingCharge(asOf.AddDays(-1), null, " "),
            AgingCharge(asOf.AddDays(-30), "Thirty", PropertyManagementCodes.ReceivableCharge),
            AgingCharge(asOf.AddDays(-31), "Thirty one", PropertyManagementCodes.ReceivableCharge),
            AgingCharge(asOf.AddDays(-60), "Sixty", PropertyManagementCodes.ReceivableCharge),
            AgingCharge(asOf.AddDays(-61), "Sixty one", PropertyManagementCodes.ReceivableCharge),
            AgingCharge(asOf.AddDays(-90), "Ninety", PropertyManagementCodes.ReceivableCharge),
            AgingCharge(asOf.AddDays(-91), "Old", PropertyManagementCodes.ReceivableCharge)
        };
        var response = DetailsResponse(charges, [], partyDisplay: null, propertyDisplay: " ", leaseDisplay: "Lease");
        var sut = new ReceivablesAgingCanonicalReportExecutor(
            ReceivablesReportReader(response).Object,
            PolicyReader());
        var leaseId = Guid.CreateVersion7();
        var definition = Definition(sut.ReportCode);
        var parameters = new Dictionary<string, string> { ["as_of_utc"] = asOf.ToString("yyyy-MM-dd") };

        var first = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), parameters: parameters, offset: -1, limit: 1), default);
        var cursorPage = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), parameters: parameters, limit: 1, cursor: first.NextCursor), default);
        var all = await sut.ExecuteAsync(definition, Request(filters: LeaseFilter(leaseId), parameters: parameters, limit: 0), default);
        var unpaged = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), parameters: parameters, disablePaging: true), default);
        var withoutTotals = await sut.ExecuteAsync(definition, Request(
            filters: LeaseFilter(leaseId), parameters: parameters,
            layout: new ReportLayoutDto(ShowGrandTotals: false)), default);

        sut.ReportCode.Should().Be(PropertyManagementSecurityDefaults.ReceivablesAgingReport);
        first.HasMore.Should().BeTrue();
        first.PrebuiltSheet!.Rows.Should().HaveCount(2);
        all.PrebuiltSheet.Should().NotBeNull();
        var allSheet = all.PrebuiltSheet!;
        allSheet.Rows.Should().HaveCount(9);
        allSheet.Rows.Take(8).Select(x => x.Cells[0].Display).Should().Contain(
            "Current", "Past due 1–30 days", "Past due 31–60 days", "Past due 61–90 days", "Past due 91+ days");
        allSheet.Rows[1].Cells[1].Display.Should().BeEmpty();
        allSheet.Rows[1].Cells[1].Action.Should().BeNull();
        allSheet.Meta!.Subtitle!.Should().Be($"Lease · {asOf:yyyy-MM-dd}");
        withoutTotals.PrebuiltSheet!.Rows.Should().HaveCount(8);
        cursorPage.Offset.Should().Be(1);
        unpaged.Limit.Should().BeGreaterThan(200);
        unpaged.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Receivables_aging_omits_totals_for_empty_response()
    {
        var sut = new ReceivablesAgingCanonicalReportExecutor(
            ReceivablesReportReader(DetailsResponse([], [])).Object,
            PolicyReader());

        var page = await sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: LeaseFilter(Guid.CreateVersion7()),
            parameters: new Dictionary<string, string> { ["as_of_utc"] = Today.ToString("yyyy-MM-dd") }), default);

        page.HasMore.Should().BeFalse();
        page.PrebuiltSheet!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Tenant_statement_covers_opening_details_closing_subtitle_and_more_pages()
    {
        var fixture = new TenantStatementFixture();
        fixture.Reader.Setup(x => x.GetPageAsync(It.Is<TenantStatementQuery>(q =>
                q.LeaseId == fixture.LeaseId && q.FromUtc == Today.AddDays(-10) && q.ToUtc == Today &&
                q.Offset == 0 && q.Limit == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Page(total: 3) with { HasMore = true });
        fixture.Reader.Setup(x => x.GetCursorPageAsync(
                It.Is<TenantStatementQuery>(q => q.Offset == 2 && q.Limit == 1),
                It.Is<TenantStatementPageCursor>(cursor => cursor.Offset == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Page(total: 2));
        fixture.SetupValidLease();
        var sut = fixture.Sut;

        var page = await sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: LeaseFilter(fixture.LeaseId),
            parameters: new Dictionary<string, string>
            {
                ["from_utc"] = Today.AddDays(-10).ToString("yyyy-MM-dd"),
                ["to_utc"] = Today.ToString("yyyy-MM-dd")
            },
            offset: -2,
            limit: 1), default);
        var cursorPage = await sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: LeaseFilter(fixture.LeaseId),
            parameters: new Dictionary<string, string>
            {
                ["from_utc"] = Today.AddDays(-10).ToString("yyyy-MM-dd"),
                ["to_utc"] = Today.ToString("yyyy-MM-dd")
            },
            limit: 1,
            cursor: page.NextCursor), default);

        sut.ReportCode.Should().Be(PropertyManagementSecurityDefaults.TenantStatementReport);
        page.HasMore.Should().BeTrue();
        page.PrebuiltSheet.Should().NotBeNull();
        var sheet = page.PrebuiltSheet!;
        sheet.Rows.Should().HaveCount(4);
        sheet.Rows.Select(x => x.SemanticRole).Should().Contain("opening_balance", "grand_total");
        sheet.Rows[1].Cells[3].Display.Should().Be("Rent");
        sheet.Rows[1].Cells[4].Display.Should().Be("10");
        sheet.Rows[1].Cells[5].Display.Should().BeNull();
        sheet.Rows[2].Cells[3].Display.Should().BeEmpty();
        sheet.Rows[2].Cells[4].Display.Should().BeNull();
        sheet.Rows[2].Cells[5].Display.Should().Be("4");
        sheet.Meta!.Subtitle.Should().Be($"Tenant · Unit 1 · Lease 1 · {Today.AddDays(-10):yyyy-MM-dd} – {Today:yyyy-MM-dd}");
        cursorPage.Offset.Should().Be(2);
        cursorPage.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_statement_covers_non_first_page_missing_catalogs_and_disabled_totals()
    {
        var fixture = new TenantStatementFixture();
        fixture.Reader.Setup(x => x.GetPageAsync(It.Is<TenantStatementQuery>(q =>
                q.Offset == 1 && q.Limit == 100), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Page(total: 2));
        fixture.SetupValidLease(
            partyResult: Task.FromException<CatalogItemDto>(new CatalogNotFoundException(fixture.PartyId)),
            propertyResult: Task.FromException<CatalogItemDto>(new CatalogNotFoundException(fixture.PropertyId)));
        var sut = fixture.Sut;

        var page = await sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: LeaseFilter(fixture.LeaseId),
            parameters: new Dictionary<string, string>
            {
                ["from_utc"] = Today.AddDays(-10).ToString("yyyy-MM-dd"),
                ["to_utc"] = Today.ToString("yyyy-MM-dd")
            },
            layout: new ReportLayoutDto(ShowGrandTotals: false),
            offset: 1,
            limit: 0), default);

        page.HasMore.Should().BeFalse();
        page.PrebuiltSheet!.Rows.Should().HaveCount(2);
        page.PrebuiltSheet.Meta!.Subtitle.Should().Be($"Lease 1 · {Today.AddDays(-10):yyyy-MM-dd} – {Today:yyyy-MM-dd}");
    }

    [Fact]
    public async Task Tenant_statement_covers_default_to_date_document_not_found_and_empty_page()
    {
        var fixture = new TenantStatementFixture();
        fixture.Reader.Setup(x => x.GetPageAsync(It.Is<TenantStatementQuery>(q =>
                q.FromUtc == null && q.Offset == 0 && q.Limit > 200), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantStatementPage([], 0,
                new TenantStatementTotals(null, Today, 0m, 0m, 0m, 0m)));
        fixture.Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, fixture.LeaseId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentNotFoundException(fixture.LeaseId));
        var sut = fixture.Sut;

        var page = await sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: LeaseFilter(fixture.LeaseId), disablePaging: true), default);

        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        page.PrebuiltSheet!.Rows.Should().ContainSingle(x => x.SemanticRole == "grand_total");
        page.PrebuiltSheet.Meta!.Subtitle.Should().StartWith("Through ");
        fixture.Catalogs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tenant_statement_rejects_reversed_date_range_before_calling_dependencies()
    {
        var fixture = new TenantStatementFixture();
        var sut = fixture.Sut;
        var act = () => sut.ExecuteAsync(Definition(sut.ReportCode), Request(
            filters: LeaseFilter(fixture.LeaseId),
            parameters: new Dictionary<string, string>
            {
                ["from_utc"] = Today.ToString("yyyy-MM-dd"),
                ["to_utc"] = Today.AddDays(-1).ToString("yyyy-MM-dd")
            }), default);

        await act.Should().ThrowAsync<ReportLayoutValidationException>();
        fixture.Reader.VerifyNoOtherCalls();
        fixture.Documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tenant_statement_rejects_each_missing_lease_payload_component()
    {
        var fixture = new TenantStatementFixture();
        fixture.Reader.Setup(x => x.GetPageAsync(It.IsAny<TenantStatementQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Page(total: 2));
        var sut = fixture.Sut;
        var definition = Definition(sut.ReportCode);
        var request = Request(
            filters: LeaseFilter(fixture.LeaseId),
            parameters: new Dictionary<string, string> { ["to_utc"] = Today.ToString("yyyy-MM-dd") });

        await fixture.AssertPayloadFailureAsync(sut, definition, request, new RecordPayload());
        await fixture.AssertPayloadFailureAsync(sut, definition, request, new RecordPayload(
            new Dictionary<string, JsonElement>(), new Dictionary<string, RecordPartPayload>()));
        await fixture.AssertPayloadFailureAsync(sut, definition, request, fixture.Payload(
            partyRows:
            [
                new Dictionary<string, JsonElement>(),
                new Dictionary<string, JsonElement> { ["is_primary"] = Json(false) }
            ]));
        await fixture.AssertPayloadFailureAsync(sut, definition, request, fixture.Payload(
            partyRows: [new Dictionary<string, JsonElement> { ["is_primary"] = Json(true) }]));
        await fixture.AssertPayloadFailureAsync(sut, definition, request, new RecordPayload(
            Fields: null,
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["parties"] = new(
                [
                    new Dictionary<string, JsonElement>
                    {
                        ["is_primary"] = Json(true),
                        ["party_id"] = Json(fixture.PartyId)
                    }
                ])
            }));
        await fixture.AssertPayloadFailureAsync(sut, definition, request, fixture.Payload(includeProperty: false));

        fixture.Catalogs.VerifyNoOtherCalls();
    }

    private static ReportDefinitionDto Definition(string code) =>
        new PropertyManagementCanonicalReportDefinitionSource().GetDefinitions()
            .Single(definition => definition.ReportCode == code);

    private static ReportExecutionRequestDto Request(
        IReadOnlyDictionary<string, ReportFilterValueDto>? filters = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        ReportLayoutDto? layout = null,
        int offset = 0,
        int limit = 200,
        string? cursor = null,
        bool disablePaging = false)
        => new(
            Layout: layout,
            Filters: filters,
            Parameters: parameters,
            Offset: offset,
            Limit: limit,
            Cursor: cursor,
            DisablePaging: disablePaging);

    private static IReadOnlyDictionary<string, ReportFilterValueDto> LeaseFilter(Guid leaseId)
        => Filters(("lease_id", Json(leaseId)));

    private static IReadOnlyDictionary<string, ReportFilterValueDto> Filters(
        params (string Code, JsonElement Value)[] values)
        => values.ToDictionary(x => x.Code, x => new ReportFilterValueDto(x.Value), StringComparer.OrdinalIgnoreCase);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static Mock<IMaintenanceQueueReader> MaintenanceReader(Action<MaintenanceQueueQuery>? capture = null)
    {
        var reader = new Mock<IMaintenanceQueueReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetPageAsync(It.IsAny<MaintenanceQueueQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MaintenanceQueueQuery query, CancellationToken _) =>
            {
                capture?.Invoke(query);
                return new MaintenanceQueuePage([], 0);
            });
        return reader;
    }

    private static MaintenanceQueueRow MaintenanceRow()
        => new(
            Guid.CreateVersion7(), "MR-1", "Leaking tap", Today.AddDays(-2), 2,
            Guid.CreateVersion7(), "Building", Guid.CreateVersion7(), "Unit 1",
            Guid.CreateVersion7(), "Plumbing", "Normal", Guid.CreateVersion7(), "Tenant",
            null, null, null, null, null, MaintenanceQueueState.Requested);

    private static ReceivablesOpenItemsDetailsResponse DetailsResponse(
        IReadOnlyList<ReceivablesOpenChargeItemDetailsDto> charges,
        IReadOnlyList<ReceivablesOpenCreditItemDetailsDto> credits,
        string? partyDisplay = "Tenant",
        string? propertyDisplay = "Property",
        string? leaseDisplay = "Lease")
        => new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), partyDisplay, Guid.CreateVersion7(), propertyDisplay,
            Guid.CreateVersion7(), leaseDisplay, charges, credits, [],
            charges.Sum(x => x.OutstandingAmount), credits.Sum(x => x.AvailableCredit));

    private static Mock<IReceivablesReportReader> ReceivablesReportReader(
        ReceivablesOpenItemsDetailsResponse response)
    {
        var rows = response.Charges.Select(x => new ReceivablesReportRow(
                true,
                x.ChargeDocumentId,
                x.DocumentType,
                x.ChargeDisplay,
                x.DueOnUtc,
                null,
                x.ChargeTypeDisplay,
                x.OriginalAmount,
                x.OutstandingAmount))
            .Concat(response.Credits.Select(x => new ReceivablesReportRow(
                false,
                x.CreditDocumentId,
                x.DocumentType,
                x.CreditDocumentDisplay,
                null,
                x.ReceivedOnUtc,
                null,
                x.OriginalAmount,
                x.AvailableCredit)))
            .ToArray();
        var reader = new Mock<IReceivablesReportReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetPageAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<ReceivablesReportMode>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, ReceivablesReportMode mode, int offset, int limit, CancellationToken _) =>
            {
                var filtered = mode == ReceivablesReportMode.Aging
                    ? rows.Where(static row => row.IsCharge).ToArray()
                    : rows;
                return new ReceivablesReportPage(
                    filtered.Skip(offset).Take(limit).ToArray(),
                    filtered.Length,
                    filtered.Where(static row => row.IsCharge).Sum(static row => row.OriginalAmount),
                    filtered.Where(static row => row.IsCharge).Sum(static row => row.OpenAmount),
                    filtered.Where(static row => !row.IsCharge).Sum(static row => row.OpenAmount),
                    response.PartyDisplay,
                    response.PropertyDisplay,
                    response.LeaseDisplay,
                    HasMore: offset == 0 && limit == 1 && filtered.Length > 1);
            });
        reader.Setup(x => x.GetCursorPageAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<ReceivablesReportMode>(),
                It.IsAny<ReceivablesReportPageCursor>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, ReceivablesReportMode mode, ReceivablesReportPageCursor cursor,
                int limit, CancellationToken _) =>
            {
                var filtered = mode == ReceivablesReportMode.Aging
                    ? rows.Where(static row => row.IsCharge).ToArray()
                    : rows;
                return new ReceivablesReportPage(
                    filtered.Skip(cursor.Offset).Take(limit).ToArray(),
                    filtered.Length,
                    filtered.Where(static row => row.IsCharge).Sum(static row => row.OriginalAmount),
                    filtered.Where(static row => row.IsCharge).Sum(static row => row.OpenAmount),
                    filtered.Where(static row => !row.IsCharge).Sum(static row => row.OpenAmount),
                    response.PartyDisplay,
                    response.PropertyDisplay,
                    response.LeaseDisplay);
            });
        return reader;
    }

    private static IPropertyManagementAccountingPolicyReader PolicyReader()
    {
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToArray();
        var reader = new Mock<IPropertyManagementAccountingPolicyReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyManagementAccountingPolicy(
                ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6], ids[7], ids[8]));
        return reader.Object;
    }

    private static ReceivablesOpenChargeItemDetailsDto AgingCharge(
        DateOnly dueOn,
        string? display,
        string documentType,
        string? chargeTypeDisplay = null)
        => new(Guid.CreateVersion7(), documentType, null, display, dueOn, null, chargeTypeDisplay, null, 10m, 8m);

    private sealed class TenantStatementFixture
    {
        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Mock<ITenantStatementReader> Reader { get; } = new(MockBehavior.Strict);
        public Mock<IDocumentService> Documents { get; } = new(MockBehavior.Strict);
        public Mock<ICatalogService> Catalogs { get; } = new(MockBehavior.Strict);
        public TenantStatementCanonicalReportExecutor Sut => new(Reader.Object, Documents.Object, Catalogs.Object);

        public TenantStatementPage Page(int total)
            => new(
                [
                    new TenantStatementRow(Today.AddDays(-2), Guid.CreateVersion7(),
                        PropertyManagementCodes.ReceivableCharge, "RC-1", "Charge", "Rent", 10m, 0m, 10m),
                    new TenantStatementRow(Today.AddDays(-1), Guid.CreateVersion7(),
                        PropertyManagementCodes.ReceivablePayment, "RP-1", "Payment", null, 0m, 4m, 6m)
                ],
                total,
                new TenantStatementTotals(Today.AddDays(-10), Today, 2m, 10m, 4m, 8m));

        public void SetupValidLease(
            Task<CatalogItemDto>? partyResult = null,
            Task<CatalogItemDto>? propertyResult = null)
        {
            Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, LeaseId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentDto(LeaseId, "Lease 1", Payload(), DocumentStatus.Draft, false));
            Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, PartyId,
                    It.IsAny<CancellationToken>()))
                .Returns(partyResult ?? Task.FromResult(new CatalogItemDto(PartyId, "Tenant", new RecordPayload(), false, false)));
            Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Property, PropertyId,
                    It.IsAny<CancellationToken>()))
                .Returns(propertyResult ?? Task.FromResult(new CatalogItemDto(PropertyId, "Unit 1", new RecordPayload(), false, false)));
        }

        public RecordPayload Payload(
            IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>? partyRows = null,
            bool includeProperty = true)
        {
            var fields = new Dictionary<string, JsonElement>();
            if (includeProperty)
                fields["property_id"] = Json(PropertyId);

            return new RecordPayload(fields, new Dictionary<string, RecordPartPayload>
            {
                ["parties"] = new(partyRows ??
                [
                    new Dictionary<string, JsonElement>
                    {
                        ["is_primary"] = Json(true),
                        ["party_id"] = Json(PartyId)
                    }
                ])
            });
        }

        public async Task AssertPayloadFailureAsync(
            TenantStatementCanonicalReportExecutor sut,
            ReportDefinitionDto definition,
            ReportExecutionRequestDto request,
            RecordPayload payload)
        {
            Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, LeaseId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentDto(LeaseId, "Lease", payload, DocumentStatus.Draft, false));

            var act = () => sut.ExecuteAsync(definition, request, default);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        }
    }
}
