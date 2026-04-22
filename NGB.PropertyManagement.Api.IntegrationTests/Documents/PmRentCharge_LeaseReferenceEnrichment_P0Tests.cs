using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmRentCharge_LeaseReferenceEnrichment_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmRentCharge_LeaseReferenceEnrichment_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RentCharge_Page_And_ById_ReturnLeaseReference_AsObject_WithDisplay_WithoutLookupRoundTrip()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        await ApplyDefaultsAsync(client);

        var party = await CreatePartyAsync(client, "John Smith");
        var unit = await CreateUnitAsync(client, "101 Main St");
        var lease = await CreateLeaseAsync(client, party.Id, unit.Id);
        var rentCharge = await CreateRentChargeAsync(client, lease.Id);

        lease.Display.Should().NotBeNullOrWhiteSpace();

        var page = await client.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            $"/api/documents/{PropertyManagementCodes.RentCharge}?offset=0&limit=50&deleted=all",
            Json);

        page.Should().NotBeNull();
        var pageItem = page!.Items.Single(x => x.Id == rentCharge.Id);
        AssertLeaseRef(pageItem.Payload.Fields!["lease_id"], lease.Id, lease.Display!);

        var byId = await client.GetFromJsonAsync<DocumentDto>(
            $"/api/documents/{PropertyManagementCodes.RentCharge}/{rentCharge.Id}",
            Json);

        byId.Should().NotBeNull();
        AssertLeaseRef(byId!.Payload.Fields!["lease_id"], lease.Id, lease.Display!);
    }

    private static void AssertLeaseRef(JsonElement leaseRef, Guid expectedId, string expectedDisplay)
    {
        leaseRef.ValueKind.Should().Be(JsonValueKind.Object);
        leaseRef.GetProperty("id").GetGuid().Should().Be(expectedId);
        leaseRef.GetProperty("display").GetString().Should().Be(expectedDisplay);
    }

    private static async Task ApplyDefaultsAsync(HttpClient client)
    {
        using var resp = await client.PostAsync("/api/admin/setup/apply-defaults", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display)
    {
        using var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display,
                    email = "john.smith@example.com",
                    phone = "+1-201-555-0101"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<CatalogItemDto> CreateUnitAsync(HttpClient client, string addressLine1)
    {
        using var buildingResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    address_line1 = addressLine1,
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        buildingResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var building = await buildingResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        building.Should().NotBeNull();

        using var unitResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    parent_property_id = building!.Id,
                    unit_no = "101"
                }
            });

        unitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var unit = await unitResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        unit.Should().NotBeNull();
        return unit!;
    }

    private static async Task<DocumentDto> CreateLeaseAsync(HttpClient client, Guid partyId, Guid unitId)
    {
        using var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = unitId,
                    start_on_utc = "2026-03-01",
                    end_on_utc = "2026-03-31",
                    rent_amount = 2700.00m,
                    memo = "Lease for enrichment test"
                },
                parts = new
                {
                    parties = new
                    {
                        rows = new object[]
                        {
                            new
                            {
                                party_id = partyId,
                                role = "PrimaryTenant",
                                is_primary = true,
                                ordinal = 1
                            }
                        }
                    }
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<DocumentDto> CreateRentChargeAsync(HttpClient client, Guid leaseId)
    {
        using var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.RentCharge}",
            new
            {
                fields = new
                {
                    lease_id = leaseId,
                    period_from_utc = "2026-03-01",
                    period_to_utc = "2026-03-31",
                    due_on_utc = "2026-03-15",
                    amount = 2700.00m,
                    memo = "Rent charge for enrichment test"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
