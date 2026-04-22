using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmProperty_BuildingsUnits_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmProperty_BuildingsUnits_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateBuilding_WhenAddressMissing_Returns400_WithFriendlyErrorCode()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    display = "No Address"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.property.building.address_required");
    }

    [Fact]
    public async Task CreateBuilding_WhenAddressMissing_ReturnsUserFriendlyMessage_AndFieldErrors()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("detail").GetString().Should().Be("Address Line 1 is required.");
        root.GetProperty("error").GetProperty("errors").GetProperty("address_line1")[0].GetString().Should().Be("Address Line 1 is required.");
    }

    [Fact]
    public async Task CreateUnit_WhenUnitNoMissing_ReturnsUserFriendlyMessage_AndFieldErrors()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var building = await CreateBuildingAsync(client);

        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    parent_property_id = building
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("detail").GetString().Should().Be("Unit number is required.");
        root.GetProperty("error").GetProperty("errors").GetProperty("unit_no")[0].GetString().Should().Be("Unit number is required.");
    }

    [Fact]
    public async Task CreateUnit_WhenParentIsUnit_Returns400_WithFriendlyErrorCode()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var building = await CreateBuildingAsync(client);
        var unit = await CreateUnitAsync(client, building, "101");

        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    display = "Unit 102",
                    parent_property_id = unit,
                    unit_no = "102"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.property.unit.parent_must_be_building");
    }

    [Fact]
    public async Task CreateUnit_WhenDuplicateUnitNoInBuilding_Returns409_WithFriendlyErrorCode()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var building = await CreateBuildingAsync(client);
        _ = await CreateUnitAsync(client, building, "101");

        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    display = "Unit 101",
                    parent_property_id = building,
                    unit_no = "101"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.property.unit_no.duplicate");
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Unit number '101' already exists in this building.");
    }

    [Fact]
    public async Task UpdateUnit_WhenSelfParent_Returns400_WithFriendlyErrorCode()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var building = await CreateBuildingAsync(client);
        var unit = await CreateUnitAsync(client, building, "101");

        var resp = await client.PutAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}/{unit}",
            new
            {
                fields = new
                {
                    parent_property_id = unit
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.property.parent_cycle");
    }

    private static async Task<Guid> CreateBuildingAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    display = "Sunset Plaza",
                    address_line1 = "123 Main St",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateUnitAsync(HttpClient client, Guid buildingId, string unitNo)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    display = $"Unit {unitNo}",
                    parent_property_id = buildingId,
                    unit_no = unitNo
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }
}
