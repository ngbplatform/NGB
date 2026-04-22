using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_PropertyManagementVertical_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_PropertyManagementVertical_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PropertyManagement_Definitions_And_Execute_Work_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var seeded = await SeedScenarioAsync(factory);

        using (var resp = await client.GetAsync("/api/report-definitions"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var defs = await resp.Content.ReadFromJsonAsync<IReadOnlyList<ReportDefinitionDto>>(Json);
            defs.Should().NotBeNull();
            defs!.Select(x => x.ReportCode).Should().Contain(new[]
            {
                "pm.building.summary",
                "pm.occupancy.summary",
                "pm.maintenance.queue",
                "pm.tenant.statement",
                "pm.receivables.aging",
                "pm.receivables.open_items",
                "pm.receivables.open_items.details"
            });
        }

        using (var resp = await client.GetAsync("/api/report-definitions/pm.occupancy.summary"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Mode.Should().Be(ReportExecutionMode.Canonical);
            def.Capabilities!.AllowsVariants.Should().BeTrue();
            def.Capabilities.AllowsXlsxExport.Should().BeTrue();
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().ContainEquivalentOf(new { Code = "as_of_utc", Label = "As of", Description = (string?)null });
            def.Filters!.Select(x => new { x.FieldCode, x.Label, x.IsRequired })
                .Should().ContainEquivalentOf(new { FieldCode = "building_id", Label = "Building", IsRequired = false });
        }

        using (var resp = await client.GetAsync("/api/report-definitions/pm.maintenance.queue"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Mode.Should().Be(ReportExecutionMode.Canonical);
            def.Capabilities!.AllowsGrandTotals.Should().BeFalse();
            def.Capabilities.AllowsVariants.Should().BeTrue();
            def.Capabilities.AllowsXlsxExport.Should().BeTrue();
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().ContainEquivalentOf(new { Code = "as_of_utc", Label = "As of", Description = (string?)null });
            def.Filters!.Select(x => new { x.FieldCode, x.Label, x.IsRequired })
                .Should().ContainEquivalentOf(new { FieldCode = "building_id", Label = "Building", IsRequired = false });
            def.Filters!.Select(x => new { x.FieldCode, x.Label, x.IsRequired })
                .Should().ContainEquivalentOf(new { FieldCode = "assigned_party_id", Label = "Assigned To", IsRequired = false });
            def.Filters!.Single(x => x.FieldCode == "queue_state").Options!
                .Select(x => x.Value)
                .Should().Contain(new[] { "Requested", "WorkOrdered", "Overdue" });
        }

        using (var resp = await client.GetAsync("/api/report-definitions/pm.tenant.statement"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Mode.Should().Be(ReportExecutionMode.Canonical);
            def.Capabilities!.AllowsGrandTotals.Should().BeTrue();
            def.Capabilities.AllowsVariants.Should().BeTrue();
            def.Capabilities.AllowsXlsxExport.Should().BeTrue();
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().ContainEquivalentOf(new { Code = "from_utc", Label = "From", Description = (string?)null });
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().ContainEquivalentOf(new { Code = "to_utc", Label = "To", Description = (string?)null });
            def.Filters!.Select(x => new { x.FieldCode, x.Label, x.IsRequired })
                .Should().Equal(new { FieldCode = "lease_id", Label = "Lease", IsRequired = true });
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.building.summary/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["as_of_utc"] = "2026-02-15"
                       },
                       Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["building_id"] = new(JsonSerializer.SerializeToElement(seeded.BuildingId))
                       },
                       Offset: 0,
                       Limit: 20)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-pm-building-summary");
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "building", "total_units", "vacancy_percent" });
            dto.Sheet.Rows.Should().ContainSingle(x => x.RowKind == ReportRowKind.Detail);
            var row = dto.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail);
            row.Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_catalog", CatalogType: "pm.property", CatalogId: seeded.BuildingId));
            row.Cells[2].Display.Should().Be("3");
            row.Cells[3].Display.Should().Be("1");
            row.Cells[4].Display.Should().Be("2");
            row.Cells[5].Display.Should().Be("66.67");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.occupancy.summary/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["as_of_utc"] = "2026-02-15"
                       },
                       Offset: 0,
                       Limit: 1)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-pm-occupancy-summary");
            dto.Total.Should().Be(2);
            dto.HasMore.Should().BeTrue();
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "building", "total_units", "occupied_units", "occupancy_percent" });
            dto.Sheet.Rows.Count(x => x.RowKind == ReportRowKind.Detail).Should().Be(1);
            dto.Sheet.Rows.Should().ContainSingle(x => x.RowKind == ReportRowKind.Total);
            var detail = dto.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail);
            detail.Cells[0].Action.Should().NotBeNull();
            detail.Cells[0].Action!.Kind.Should().Be("open_catalog");
            detail.Cells[2].Display.Should().Be("3");
            detail.Cells[3].Display.Should().Be("1");
            detail.Cells[4].Display.Should().Be("2");
            detail.Cells[5].Display.Should().Be("33.33");

            var totalRow = dto.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Total);
            totalRow.Cells[2].Display.Should().Be("5");
            totalRow.Cells[3].Display.Should().Be("3");
            totalRow.Cells[4].Display.Should().Be("2");
            totalRow.Cells[5].Display.Should().Be("60");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.occupancy.summary/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["as_of_utc"] = "2026-02-15"
                       },
                       Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["building_id"] = new(JsonSerializer.SerializeToElement(seeded.SecondBuildingId))
                       },
                       Offset: 0,
                       Limit: 20)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Total.Should().Be(1);
            dto.HasMore.Should().BeFalse();
            var detail = dto.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail);
            detail.Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_catalog", CatalogType: "pm.property", CatalogId: seeded.SecondBuildingId));
            detail.Cells[2].Display.Should().Be("2");
            detail.Cells[3].Display.Should().Be("2");
            detail.Cells[4].Display.Should().Be("0");
            detail.Cells[5].Display.Should().Be("100");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.maintenance.queue/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["as_of_utc"] = "2026-02-15"
                       },
                       Offset: 0,
                       Limit: 2)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-pm-maintenance-queue");
            dto.Total.Should().Be(3);
            dto.HasMore.Should().BeTrue();
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "queue_state", "request", "subject", "work_order", "assigned_to", "due_by_utc" });
            dto.Sheet.Rows.Should().OnlyContain(x => x.RowKind == ReportRowKind.Detail);
            dto.Sheet.Rows.Should().HaveCount(2);

            var requestedRow = dto.Sheet.Rows[0];
            requestedRow.Cells[0].Display.Should().Be("Requested");
            requestedRow.Cells[1].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_document", DocumentType: PropertyManagementCodes.MaintenanceRequest, DocumentId: seeded.RequestedOnlyRequestId));
            requestedRow.Cells[2].Display.Should().Be("Hall light out");
            requestedRow.Cells[5].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_catalog", CatalogType: PropertyManagementCodes.Property, CatalogId: seeded.BuildingId));
            requestedRow.Cells[6].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_catalog", CatalogType: PropertyManagementCodes.Property, CatalogId: seeded.Unit2Id));
            requestedRow.Cells[10].Display.Should().BeNull();
            requestedRow.Cells[11].Display.Should().BeNull();
            requestedRow.Cells[12].Display.Should().BeNull();

            var overdueRow = dto.Sheet.Rows[1];
            overdueRow.Cells[0].Display.Should().Be("Overdue");
            overdueRow.Cells[1].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_document", DocumentType: PropertyManagementCodes.MaintenanceRequest, DocumentId: seeded.OverdueRequestId));
            overdueRow.Cells[10].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_document", DocumentType: PropertyManagementCodes.WorkOrder, DocumentId: seeded.OverdueWorkOrderId));
            overdueRow.Cells[11].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_catalog", CatalogType: PropertyManagementCodes.Party, CatalogId: seeded.VendorId));
            overdueRow.Cells[12].Display.Should().Be("2026-02-12");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.maintenance.queue/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["as_of_utc"] = "2026-02-15"
                       },
                       Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["building_id"] = new(JsonSerializer.SerializeToElement(seeded.BuildingId))
                       },
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Total.Should().Be(2);
            dto.HasMore.Should().BeFalse();
            dto.Sheet.Rows.Select(x => x.Cells[0].Display).Should().BeEquivalentTo(["Requested", "Overdue"]);
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.maintenance.queue/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["as_of_utc"] = "2026-02-15"
                       },
                       Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["queue_state"] = new(JsonSerializer.SerializeToElement("Overdue"))
                       },
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Total.Should().Be(1);
            dto.Sheet.Rows.Should().ContainSingle();
            var row = dto.Sheet.Rows.Single();
            row.Cells[0].Display.Should().Be("Overdue");
            row.Cells[1].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_document", DocumentType: PropertyManagementCodes.MaintenanceRequest, DocumentId: seeded.OverdueRequestId));
            row.Cells[10].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_document", DocumentType: PropertyManagementCodes.WorkOrder, DocumentId: seeded.OverdueWorkOrderId));
            row.Cells[11].Action.Should().BeEquivalentTo(new ReportCellActionDto("open_catalog", CatalogType: PropertyManagementCodes.Party, CatalogId: seeded.VendorId));
            row.Cells[12].Display.Should().Be("2026-02-12");
        }

        var receivablesFilters = new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["lease_id"] = new(JsonSerializer.SerializeToElement(seeded.LeaseId))
        };


        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.tenant.statement/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-04-01",
                           ["to_utc"] = "2026-04-30"
                       },
                       Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["lease_id"] = new(JsonSerializer.SerializeToElement(seeded.StatementLeaseId))
                       },
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["executor"].Should().Be("canonical-pm-tenant-statement");
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "occurred_on_utc", "document", "entry_type", "running_balance" });
            dto.Total.Should().Be(3);
            dto.HasMore.Should().BeFalse();
            dto.Sheet.Rows.Should().HaveCount(5);

            var opening = dto.Sheet.Rows[0];
            opening.SemanticRole.Should().Be("opening_balance");
            opening.Cells[1].Display.Should().Be("Opening balance");
            opening.Cells[6].Display.Should().Be("400");

            var utilityCharge = dto.Sheet.Rows[1];
            utilityCharge.Cells[0].Display.Should().Be("2026-04-05");
            utilityCharge.Cells[1].Action.Should().NotBeNull();
            utilityCharge.Cells[1].Action!.Kind.Should().Be("open_document");
            utilityCharge.Cells[2].Display.Should().Be("Charge");
            utilityCharge.Cells[3].Display.Should().Be("Utility");
            utilityCharge.Cells[4].Display.Should().Be("150");
            utilityCharge.Cells[6].Display.Should().Be("550");

            var creditMemo = dto.Sheet.Rows[2];
            creditMemo.Cells[0].Display.Should().Be("2026-04-10");
            creditMemo.Cells[2].Display.Should().Be("Credit memo");
            creditMemo.Cells[3].Display.Should().Be("Utility");
            creditMemo.Cells[5].Display.Should().Be("50");
            creditMemo.Cells[6].Display.Should().Be("500");

            var returnedPayment = dto.Sheet.Rows[3];
            returnedPayment.Cells[0].Display.Should().Be("2026-04-15");
            returnedPayment.Cells[2].Display.Should().Be("Returned payment");
            returnedPayment.Cells[4].Display.Should().Be("100");
            returnedPayment.Cells[6].Display.Should().Be("600");

            var totalRow = dto.Sheet.Rows[4];
            totalRow.RowKind.Should().Be(ReportRowKind.Total);
            totalRow.Cells[1].Display.Should().Be("Closing balance");
            totalRow.Cells[4].Display.Should().Be("250");
            totalRow.Cells[5].Display.Should().Be("50");
            totalRow.Cells[6].Display.Should().Be("600");
        }
        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.receivables.aging/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["as_of_utc"] = "2026-02-10"
                       },
                       Filters: receivablesFilters,
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["executor"].Should().Be("canonical-pm-receivables-aging");
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "bucket", "charge", "outstanding_amount" });
            dto.Total.Should().Be(1);
            var detail = dto.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail);
            detail.Cells[0].Display.Should().Be("Current");
            detail.Cells[1].Action.Should().NotBeNull();
            detail.Cells[1].Action!.Kind.Should().Be("open_document");
            detail.Cells[3].Display.Should().Be("2026-04-05");
            detail.Cells[6].Display.Should().Be("30");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.receivables.open_items/execute",
                   new ReportExecutionRequestDto(
                       Filters: receivablesFilters,
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["executor"].Should().Be("canonical-pm-receivables-open-items");
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "kind", "outstanding_amount", "available_credit" });
            dto.Total.Should().Be(2);
            dto.Sheet.Rows.Count(x => x.RowKind == ReportRowKind.Detail).Should().Be(2);
            dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Detail && x.Cells[0].Display == "Charge" && x.Cells[1].Action != null && x.Cells[1].Action!.Kind == "open_document" && x.Cells[2].Display == "30");
            dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Detail && x.Cells[0].Display == "Credit" && x.Cells[1].Action != null && x.Cells[1].Action!.Kind == "open_document" && x.Cells[3].Display == "50");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/pm.receivables.open_items.details/execute",
                   new ReportExecutionRequestDto(
                       Filters: receivablesFilters,
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["executor"].Should().Be("canonical-pm-receivables-open-items-details");
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "due_on_utc", "received_on_utc", "available_credit" });
            dto.Total.Should().Be(2);
            dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Detail && x.Cells[0].Display == "Charge" && x.Cells[1].Action != null && x.Cells[1].Action!.Kind == "open_document" && x.Cells[2].Display == "2026-04-05" && x.Cells[6].Display == "30");
            dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Detail && x.Cells[0].Display == "Credit" && x.Cells[1].Action != null && x.Cells[1].Action!.Kind == "open_document" && x.Cells[3].Display == "2026-02-07" && x.Cells[7].Display == "50");
        }
    }

    [Fact]
    public async Task BuildingSummary_When_Filter_Uses_Unit_Returns_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var seeded = await SeedScenarioAsync(factory);

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/pm.building.summary/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-02-15"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["building_id"] = new(JsonSerializer.SerializeToElement(seeded.UnitId))
                },
                Offset: 0,
                Limit: 20));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("Selected property must be a building.");
    }

    [Fact]
    public async Task OccupancySummary_When_Filter_Uses_Unit_Returns_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var seeded = await SeedScenarioAsync(factory);

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/pm.occupancy.summary/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-02-15"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["building_id"] = new(JsonSerializer.SerializeToElement(seeded.UnitId))
                },
                Offset: 0,
                Limit: 20));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("Selected property must be a building.");
    }

    [Fact]
    public async Task MaintenanceQueue_When_Building_Filter_Uses_Unit_Returns_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var seeded = await SeedScenarioAsync(factory);

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/pm.maintenance.queue/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-02-15"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["building_id"] = new(JsonSerializer.SerializeToElement(seeded.UnitId))
                },
                Offset: 0,
                Limit: 20));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("Select a valid Building.");
    }

    [Fact]
    public async Task MaintenanceQueue_When_Property_Filter_Is_Not_A_Property_Returns_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var seeded = await SeedScenarioAsync(factory);

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/pm.maintenance.queue/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-02-15"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(seeded.PartyId))
                },
                Offset: 0,
                Limit: 20));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("Select a valid Property.");
    }

    [Fact]
    public async Task AccountingReports_When_Property_Filter_Is_Not_A_Property_Return_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var seeded = await SeedScenarioAsync(factory);

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.trial_balance/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(seeded.PartyId), IncludeDescendants: true)
                },
                Offset: 0,
                Limit: 20));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("Select a valid Property.");
    }

    private static async Task<SeededScenario> SeedScenarioAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant" }), CancellationToken.None);
        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "FixIt Vendor", is_tenant = false, is_vendor = true }), CancellationToken.None);

        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "1 Hudson St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var unit1 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var unit2 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "102"
        }), CancellationToken.None);

        var unit3 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "103"
        }), CancellationToken.None);

        var secondBuilding = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "9 Washington St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var secondUnit1 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = secondBuilding.Id,
            unit_no = "201"
        }), CancellationToken.None);

        var secondUnit2 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = secondBuilding.Id,
            unit_no = "202"
        }), CancellationToken.None);

        var activeLease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit1.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, activeLease.Id, CancellationToken.None);

        var endedLease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit2.Id,
            start_on_utc = "2026-01-01",
            end_on_utc = "2026-01-31",
            rent_amount = "900.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, endedLease.Id, CancellationToken.None);

        var futureLease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit3.Id,
            start_on_utc = "2026-03-01",
            rent_amount = "1100.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, futureLease.Id, CancellationToken.None);

        var secondActiveLease1 = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = secondUnit1.Id,
            start_on_utc = "2025-12-15",
            rent_amount = "1300.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, secondActiveLease1.Id, CancellationToken.None);

        var secondActiveLease2 = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = secondUnit2.Id,
            start_on_utc = "2026-02-10",
            rent_amount = "1400.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, secondActiveLease2.Id, CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var utilityType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));
        var maintenanceCategories = await catalogs.GetPageAsync(PropertyManagementCodes.MaintenanceCategory, new PageRequestDto(0, 50, null), CancellationToken.None);
        var plumbingCategory = maintenanceCategories.Items.Single(x => string.Equals(x.Display, "Plumbing", StringComparison.OrdinalIgnoreCase));
        var electricalCategory = maintenanceCategories.Items.Single(x => string.Equals(x.Display, "Electrical", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = activeLease.Id,
            charge_type_id = utilityType.Id,
            due_on_utc = "2026-04-05",
            amount = "100.00"
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

        var statementRentCharge = await documents.CreateDraftAsync(PropertyManagementCodes.RentCharge, Payload(new
        {
            lease_id = secondActiveLease1.Id,
            period_from_utc = "2026-03-01",
            period_to_utc = "2026-03-31",
            due_on_utc = "2026-03-05",
            amount = "1000.00"
        }), CancellationToken.None);
        statementRentCharge = await documents.PostAsync(PropertyManagementCodes.RentCharge, statementRentCharge.Id, CancellationToken.None);

        var statementPayment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = secondActiveLease1.Id,
            received_on_utc = "2026-03-10",
            amount = "600.00"
        }), CancellationToken.None);
        statementPayment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, statementPayment.Id, CancellationToken.None);

        var statementUtilityCharge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = secondActiveLease1.Id,
            charge_type_id = utilityType.Id,
            due_on_utc = "2026-04-05",
            amount = "150.00"
        }), CancellationToken.None);
        statementUtilityCharge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, statementUtilityCharge.Id, CancellationToken.None);

        var statementCreditMemo = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCreditMemo, Payload(new
        {
            lease_id = secondActiveLease1.Id,
            charge_type_id = utilityType.Id,
            credited_on_utc = "2026-04-10",
            amount = "50.00"
        }), CancellationToken.None);
        statementCreditMemo = await documents.PostAsync(PropertyManagementCodes.ReceivableCreditMemo, statementCreditMemo.Id, CancellationToken.None);

        var statementReturnedPayment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableReturnedPayment, Payload(new
        {
            original_payment_id = statementPayment.Id,
            returned_on_utc = "2026-04-15",
            amount = "100.00"
        }), CancellationToken.None);
        statementReturnedPayment = await documents.PostAsync(PropertyManagementCodes.ReceivableReturnedPayment, statementReturnedPayment.Id, CancellationToken.None);

        var statementOffsetPayment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = secondActiveLease1.Id,
            received_on_utc = "2026-05-01",
            amount = "600.00"
        }), CancellationToken.None);
        statementOffsetPayment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, statementOffsetPayment.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = activeLease.Id,
            received_on_utc = "2026-02-07",
            amount = "120.00"
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
        {
            credit_document_id = payment.Id,
            charge_document_id = charge.Id,
            applied_on_utc = "2026-02-07",
            amount = "70.00"
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);

        var overdueRequest = await documents.CreateDraftAsync(PropertyManagementCodes.MaintenanceRequest, Payload(new
        {
            property_id = unit1.Id,
            party_id = party.Id,
            category_id = plumbingCategory.Id,
            priority = "high",
            subject = "Leaking sink",
            description = "Water under the sink.",
            requested_at_utc = "2026-02-10"
        }), CancellationToken.None);
        overdueRequest = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, overdueRequest.Id, CancellationToken.None);

        var overdueWorkOrder = await documents.CreateDraftAsync(PropertyManagementCodes.WorkOrder, Payload(new
        {
            request_id = overdueRequest.Id,
            assigned_party_id = vendor.Id,
            scope_of_work = "Inspect and replace trap.",
            due_by_utc = "2026-02-12",
            cost_responsibility = "owner"
        }), CancellationToken.None);
        overdueWorkOrder = await documents.PostAsync(PropertyManagementCodes.WorkOrder, overdueWorkOrder.Id, CancellationToken.None);

        var requestedOnlyRequest = await documents.CreateDraftAsync(PropertyManagementCodes.MaintenanceRequest, Payload(new
        {
            property_id = unit2.Id,
            party_id = party.Id,
            category_id = electricalCategory.Id,
            priority = "normal",
            subject = "Hall light out",
            description = "Hallway light fixture is not working.",
            requested_at_utc = "2026-02-14"
        }), CancellationToken.None);
        requestedOnlyRequest = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, requestedOnlyRequest.Id, CancellationToken.None);

        var workOrderedRequest = await documents.CreateDraftAsync(PropertyManagementCodes.MaintenanceRequest, Payload(new
        {
            property_id = secondUnit1.Id,
            party_id = party.Id,
            category_id = plumbingCategory.Id,
            priority = "low",
            subject = "Dripping faucet",
            description = "Bathroom faucet keeps dripping.",
            requested_at_utc = "2026-02-09"
        }), CancellationToken.None);
        workOrderedRequest = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, workOrderedRequest.Id, CancellationToken.None);

        var workOrderedWorkOrder = await documents.CreateDraftAsync(PropertyManagementCodes.WorkOrder, Payload(new
        {
            request_id = workOrderedRequest.Id,
            assigned_party_id = vendor.Id,
            scope_of_work = "Tighten fixture and test.",
            due_by_utc = "2026-02-18",
            cost_responsibility = "owner"
        }), CancellationToken.None);
        workOrderedWorkOrder = await documents.PostAsync(PropertyManagementCodes.WorkOrder, workOrderedWorkOrder.Id, CancellationToken.None);

        var completedRequest = await documents.CreateDraftAsync(PropertyManagementCodes.MaintenanceRequest, Payload(new
        {
            property_id = secondUnit2.Id,
            party_id = party.Id,
            category_id = electricalCategory.Id,
            priority = "emergency",
            subject = "Breaker outage",
            description = "Panel lost power.",
            requested_at_utc = "2026-02-08"
        }), CancellationToken.None);
        completedRequest = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, completedRequest.Id, CancellationToken.None);

        var completedWorkOrder = await documents.CreateDraftAsync(PropertyManagementCodes.WorkOrder, Payload(new
        {
            request_id = completedRequest.Id,
            assigned_party_id = vendor.Id,
            scope_of_work = "Restore breaker and inspect wiring.",
            due_by_utc = "2026-02-11",
            cost_responsibility = "owner"
        }), CancellationToken.None);
        completedWorkOrder = await documents.PostAsync(PropertyManagementCodes.WorkOrder, completedWorkOrder.Id, CancellationToken.None);

        var completion = await documents.CreateDraftAsync(PropertyManagementCodes.WorkOrderCompletion, Payload(new
        {
            work_order_id = completedWorkOrder.Id,
            closed_at_utc = "2026-02-11",
            outcome = "completed",
            resolution_notes = "Replaced breaker and restored service."
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.WorkOrderCompletion, completion.Id, CancellationToken.None);

        return new SeededScenario(
            BuildingId: building.Id,
            SecondBuildingId: secondBuilding.Id,
            UnitId: unit1.Id,
            Unit2Id: unit2.Id,
            LeaseId: activeLease.Id,
            StatementLeaseId: secondActiveLease1.Id,
            PartyId: party.Id,
            VendorId: vendor.Id,
            OverdueRequestId: overdueRequest.Id,
            OverdueWorkOrderId: overdueWorkOrder.Id,
            RequestedOnlyRequestId: requestedOnlyRequest.Id);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            dict[property.Name] = property.Value;

        return new RecordPayload(dict, parts);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record SeededScenario(
        Guid BuildingId,
        Guid SecondBuildingId,
        Guid UnitId,
        Guid Unit2Id,
        Guid LeaseId,
        Guid StatementLeaseId,
        Guid PartyId,
        Guid VendorId,
        Guid OverdueRequestId,
        Guid OverdueWorkOrderId,
        Guid RequestedOnlyRequestId);
}
