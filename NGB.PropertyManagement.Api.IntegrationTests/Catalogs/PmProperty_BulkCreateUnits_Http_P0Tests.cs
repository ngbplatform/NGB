using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Catalogs;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmProperty_BulkCreateUnits_Http_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmProperty_BulkCreateUnits_Http_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BulkCreateUnits_WhenCalledTwice_IsIdempotentAndReturnsDuplicates()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var buildingId = await CreateBuildingAsync(client);

        var req = new PropertyBulkCreateUnitsRequest
        {
            BuildingId = buildingId,
            FromInclusive = 1,
            ToInclusive = 5,
            Step = 1,
            UnitNoFormat = "{0:000}",
            FloorSize = null
        };

        // 1) First run creates.
        var r1 = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units", req, Json);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var resp1 = await r1.Content.ReadFromJsonAsync<PropertyBulkCreateUnitsResponse>(Json);
        resp1.Should().NotBeNull();
        resp1!.RequestedCount.Should().Be(5);
        resp1.CreatedCount.Should().Be(5);
        resp1.DuplicateCount.Should().Be(0);
        resp1.CreatedIds.Should().HaveCount(5);

        // Validate created units exist.
        var page = await client.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(
            $"/api/catalogs/pm.property?kind=Unit&parent_property_id={buildingId}&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().HaveCount(5);

        // 2) Second run reports duplicates and creates none.
        var r2 = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units", req, Json);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var resp2 = await r2.Content.ReadFromJsonAsync<PropertyBulkCreateUnitsResponse>(Json);
        resp2.Should().NotBeNull();
        resp2!.RequestedCount.Should().Be(5);
        resp2.CreatedCount.Should().Be(0);
        resp2.DuplicateCount.Should().Be(5);
        resp2.CreatedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task BulkCreateUnits_DryRun_ReturnsPreviewAndWouldCreateCounts_AndDoesNotWrite()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var buildingId = await CreateBuildingAsync(client);

        var req = new PropertyBulkCreateUnitsRequest
        {
            BuildingId = buildingId,
            FromInclusive = 1,
            ToInclusive = 5,
            Step = 1,
            UnitNoFormat = "{0:000}",
            FloorSize = null
        };

        // 1) Dry-run before any writes.
        var d1 = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units?dryRun=true", req, Json);
        d1.StatusCode.Should().Be(HttpStatusCode.OK);
        var dr1 = await d1.Content.ReadFromJsonAsync<PropertyBulkCreateUnitsResponse>(Json);
        dr1.Should().NotBeNull();
        dr1!.IsDryRun.Should().BeTrue();
        dr1.RequestedCount.Should().Be(5);
        dr1.CreatedCount.Should().Be(0);
        dr1.DuplicateCount.Should().Be(0);
        dr1.WouldCreateCount.Should().Be(5);
        dr1.CreatedIds.Should().BeEmpty();
        dr1.PreviewUnitNosSample.Should().HaveCount(5);

        // Ensure no units were created.
        var page0 = await client.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(
            $"/api/catalogs/pm.property?kind=Unit&parent_property_id={buildingId}&offset=0&limit=50",
            Json);

        page0.Should().NotBeNull();
        page0!.Items.Should().BeEmpty();

        // 2) Real run writes.
        var r = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units", req, Json);
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3) Dry-run after writes reports duplicates.
        var d2 = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units?dryRun=true", req, Json);
        d2.StatusCode.Should().Be(HttpStatusCode.OK);
        var dr2 = await d2.Content.ReadFromJsonAsync<PropertyBulkCreateUnitsResponse>(Json);
        dr2.Should().NotBeNull();
        dr2!.IsDryRun.Should().BeTrue();
        dr2.RequestedCount.Should().Be(5);
        dr2.CreatedCount.Should().Be(0);
        dr2.DuplicateCount.Should().Be(5);
        dr2.WouldCreateCount.Should().Be(0);
    }

    [Fact]
    public async Task BulkCreateUnits_WhenBuildingMissing_ReturnsUserFriendlyValidation()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var req = new PropertyBulkCreateUnitsRequest
        {
            BuildingId = Guid.Empty,
            FromInclusive = 1,
            ToInclusive = 5,
            Step = 1,
            UnitNoFormat = "{0:000}"
        };

        var resp = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units", req, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.property.bulk_create_units.building_required");
        root.GetProperty("detail").GetString().Should().Be("Building is required.");
        root.GetProperty("error").GetProperty("errors").GetProperty("buildingId")[0].GetString().Should().Be("Building is required.");
    }

    [Fact]
    public async Task BulkCreateUnits_WhenFormatMissesNumberPlaceholder_ReturnsUserFriendlyValidation()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var buildingId = await CreateBuildingAsync(client);
        var req = new PropertyBulkCreateUnitsRequest
        {
            BuildingId = buildingId,
            FromInclusive = 1,
            ToInclusive = 5,
            Step = 1,
            UnitNoFormat = "Unit-"
        };

        var resp = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units", req, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.property.bulk_create_units.unit_no_format_missing_number_placeholder");
        root.GetProperty("detail").GetString().Should().Be("Unit number format must include {0}.");
        root.GetProperty("error").GetProperty("errors").GetProperty("unitNoFormat")[0].GetString().Should().Be("Unit number format must include {0}.");
    }

    [Fact]
    public async Task BulkCreateUnits_WhenFormatIsInvalid_ReturnsUserFriendlyValidation()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var buildingId = await CreateBuildingAsync(client);
        var req = new PropertyBulkCreateUnitsRequest
        {
            BuildingId = buildingId,
            FromInclusive = 1,
            ToInclusive = 5,
            Step = 1,
            UnitNoFormat = "{0:000}-{2}"
        };

        var resp = await client.PostAsJsonAsync("/api/catalogs/pm.property/bulk-create-units", req, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.property.bulk_create_units.unit_no_format_invalid");
        root.GetProperty("detail").GetString().Should().Be("Unit number format is invalid.");
        root.GetProperty("error").GetProperty("errors").GetProperty("unitNoFormat")[0].GetString().Should().Be("Use a format like {0}, {0:0000}, or {1}-{0:000}.");
    }

    private static async Task<Guid> CreateBuildingAsync(HttpClient client)
    {
        var createResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    // display is DB-computed in PM-PROP-DISPLAY-01, but user override is allowed.
                    address_line1 = "123 Main St",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);
        return created.Id;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
