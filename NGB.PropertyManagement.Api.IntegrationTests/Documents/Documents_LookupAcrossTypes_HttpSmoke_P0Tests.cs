using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class Documents_LookupAcrossTypes_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public Documents_LookupAcrossTypes_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lookup_And_ByIds_Across_Document_Types_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreatePartyAsync(client, "Lookup Tenant");
        var building = await CreateBuildingAsync(client, "555 Lookup Ave");
        var unit = await CreateUnitAsync(client, building.Id, "2B");
        var lease = await CreateLeaseAsync(client, party.Id, unit.Id);

        using (var searchResp = await client.PostAsJsonAsync(
                   "/api/documents/lookup",
                   new DocumentLookupAcrossTypesRequestDto(
                       DocumentTypes: [PropertyManagementCodes.WorkOrder, PropertyManagementCodes.Lease],
                       Query: "Lookup Ave",
                       PerTypeLimit: 20,
                       ActiveOnly: false)))
        {
            searchResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var items = await searchResp.Content.ReadFromJsonAsync<IReadOnlyList<DocumentLookupDto>>(Json);
            items.Should().NotBeNull();
            items!.Should().Contain(x => x.Id == lease.Id && x.DocumentType == PropertyManagementCodes.Lease);
        }

        using (var byIdsResp = await client.PostAsJsonAsync(
                   "/api/documents/lookup/by-ids",
                   new DocumentLookupByIdsRequestDto(
                       DocumentTypes: [PropertyManagementCodes.WorkOrder, PropertyManagementCodes.Lease],
                       Ids: [lease.Id, Guid.CreateVersion7()])))
        {
            byIdsResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var items = await byIdsResp.Content.ReadFromJsonAsync<IReadOnlyList<DocumentLookupDto>>(Json);
            items.Should().NotBeNull();
            var leaseItem = items!.Single(x => x.Id == lease.Id);
            leaseItem.DocumentType.Should().Be(PropertyManagementCodes.Lease);
            leaseItem.Status.Should().Be(DocumentStatus.Draft);
            leaseItem.Display.Should().Contain("Lookup Ave");
        }
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display,
                    is_tenant = true
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<CatalogItemDto> CreateBuildingAsync(HttpClient client, string addressLine1)
    {
        var resp = await client.PostAsJsonAsync(
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

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<CatalogItemDto> CreateUnitAsync(HttpClient client, Guid parentPropertyId, string unitNo)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    parent_property_id = parentPropertyId,
                    unit_no = unitNo
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<DocumentDto> CreateLeaseAsync(HttpClient client, Guid partyId, Guid propertyId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = propertyId,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-01-31",
                    rent_amount = 1450.00m,
                    due_day = 5,
                    memo = "Lookup test lease"
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

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
