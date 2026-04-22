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
public sealed class PmReporting_GeneralJournal_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_GeneralJournal_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GeneralJournal_Dimension_Filtered_Paging_Is_Stable_And_Duplicate_Free_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        var ctx = await SeedGeneralJournalScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var firstPage = await ExecuteAsync(
            client,
            propertyIds: [ctx.BuildingAId],
            includeDescendants: true,
            cursor: null,
            limit: 2);

        firstPage.HasMore.Should().BeTrue();
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        firstPage.Sheet.Rows.Should().HaveCount(2);
        firstPage.Sheet.Rows.Select(GetAmount).Should().NotContain(99m);

        var secondPage = await ExecuteAsync(
            client,
            propertyIds: [ctx.BuildingAId],
            includeDescendants: true,
            cursor: firstPage.NextCursor,
            limit: 2);

        secondPage.HasMore.Should().BeFalse();
        secondPage.Sheet.Rows.Should().HaveCount(1);
        secondPage.Sheet.Rows.Select(GetAmount).Should().NotContain(99m);

        var allKeys = firstPage.Sheet.Rows.Select(BuildRowKey)
            .Concat(secondPage.Sheet.Rows.Select(BuildRowKey))
            .ToList();

        allKeys.Should().OnlyHaveUniqueItems();
        allKeys.Should().HaveCount(3);
    }

    [Fact]
    public async Task GeneralJournal_Property_Scope_Filters_In_SQL_Read_Path_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        var ctx = await SeedGeneralJournalScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await ExecuteAsync(
            client,
            propertyIds: [ctx.UnitA1Id],
            includeDescendants: false,
            cursor: null,
            limit: 50);

        response.Sheet.Rows.Should().HaveCount(2);
        response.Sheet.Rows.Select(GetAmount).Should().Equal(10m, 30m);
        response.HasMore.Should().BeFalse();
    }

    private static async Task<ReportExecutionResponseDto> ExecuteAsync(
        HttpClient client,
        IReadOnlyList<Guid> propertyIds,
        bool includeDescendants,
        string? cursor,
        int limit)
    {
        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.general_journal/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(propertyIds), IncludeDescendants: includeDescendants)
                },
                Cursor: cursor,
                Offset: 0,
                Limit: limit));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static decimal GetAmount(ReportSheetRowDto row)
    {
        var cell = row.Cells.Single(x => x.ValueType == "decimal");
        var value = cell.Value;
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Number
            ? value.Value.GetDecimal()
            : decimal.Parse(cell.Display!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string BuildRowKey(ReportSheetRowDto row)
        => string.Join("|", row.Cells.Select(CellKey));

    private static string CellKey(ReportCellDto cell)
    {
        if (!string.IsNullOrWhiteSpace(cell.Display))
            return cell.Display;

        var value = cell.Value;
        if (!value.HasValue)
            return string.Empty;

        if (value.Value.ValueKind == JsonValueKind.String)
            return value.Value.GetString() ?? string.Empty;

        return value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? string.Empty
            : value.Value.ToString();
    }

    private static async Task<SeedContext> SeedGeneralJournalScenarioAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var utilityType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        var partyA = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant A" }), CancellationToken.None);
        var buildingA = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "1 Hudson St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);
        var unitA1 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = buildingA.Id,
            unit_no = "101"
        }), CancellationToken.None);
        var unitA2 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = buildingA.Id,
            unit_no = "102"
        }), CancellationToken.None);

        var leaseA1 = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unitA1.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(partyA.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, leaseA1.Id, CancellationToken.None);

        var leaseA2 = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unitA2.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1200.00"
        }, LeaseParts.PrimaryTenant(partyA.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, leaseA2.Id, CancellationToken.None);

        await CreateAndPostChargeAsync(documents, utilityType.Id, leaseA1.Id, "2026-02-05", 10m);
        await CreateAndPostChargeAsync(documents, utilityType.Id, leaseA2.Id, "2026-03-05", 20m);
        await CreateAndPostChargeAsync(documents, utilityType.Id, leaseA1.Id, "2026-04-05", 30m);

        var partyB = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant B" }), CancellationToken.None);
        var buildingB = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "99 River Rd",
            city = "Weehawken",
            state = "NJ",
            zip = "07086"
        }), CancellationToken.None);
        var unitB1 = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = buildingB.Id,
            unit_no = "201"
        }), CancellationToken.None);
        var leaseB1 = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unitB1.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1500.00"
        }, LeaseParts.PrimaryTenant(partyB.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, leaseB1.Id, CancellationToken.None);
        await CreateAndPostChargeAsync(documents, utilityType.Id, leaseB1.Id, "2026-03-15", 99m);

        return new SeedContext(buildingA.Id, unitA1.Id);
    }

    private static async Task CreateAndPostChargeAsync(IDocumentService documents, Guid chargeTypeId, Guid leaseId, string dueOnUtc, decimal amount)
    {
        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = leaseId,
            charge_type_id = chargeTypeId,
            due_on_utc = dueOnUtc,
            amount = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);
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

    private sealed record SeedContext(Guid BuildingAId, Guid UnitA1Id);
}
