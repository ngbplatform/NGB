using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmProperty_Display_Computed_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    // ASP.NET Core API is configured with JsonStringEnumConverter (see NGB.Api).
    // HttpClient JSON helpers use default options without enum-string support,
    // so tests must opt-in to the same enum serialization contract.
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmProperty_Display_Computed_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateAndUpdate_WhenDisplayIsMissing_ComputesDisplay_FromAddressOrParent()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // PM API enables HTTPS redirection. Using https scheme avoids redirects in TestServer.
            BaseAddress = new Uri("https://localhost")
        });

        // 1) Create Building without display.
        var buildingCreate = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    address_line1 = "123 Main St",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        buildingCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        var building = await buildingCreate.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        building.Should().NotBeNull();
        building!.Display.Should().Be("123 Main St, Hoboken, NJ 07030");

        // 2) Create Unit without display.
        var unitCreate = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    parent_property_id = building.Id,
                    unit_no = "A-1"
                }
            });

        unitCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        var unit = await unitCreate.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        unit.Should().NotBeNull();
        unit!.Display.Should().Be("123 Main St, Hoboken, NJ 07030 #A-1");

        // 3) Update Building address without display -> recompute.
        var update = await client.PutAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}/{building.Id}",
            new
            {
                fields = new
                {
                    address_line1 = "124 Main St"
                }
            });

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        updated.Should().NotBeNull();
        updated!.Display.Should().Be("124 Main St, Hoboken, NJ 07030");
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
