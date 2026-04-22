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
public sealed class DocumentController_RouteContracts_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public DocumentController_RouteContracts_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_And_Derive_Routes_Work_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateHttpsClient(factory);

        using (var metadataResponse = await client.GetAsync("/api/documents/metadata"))
        {
            metadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await metadataResponse.Content.ReadFromJsonAsync<IReadOnlyList<DocumentTypeMetadataDto>>(Json);
            metadata.Should().NotBeNull();
            metadata!.Should().Contain(x => x.DocumentType == PropertyManagementCodes.Lease);
            metadata.Should().Contain(x => x.DocumentType == PropertyManagementCodes.RentCharge);
        }

        using (var typeMetadataResponse = await client.GetAsync($"/api/documents/{PropertyManagementCodes.Lease}/metadata"))
        {
            typeMetadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await typeMetadataResponse.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
            metadata.Should().NotBeNull();
            metadata!.DocumentType.Should().Be(PropertyManagementCodes.Lease);
        }

        var party = await CreatePartyAsync(client, "Route Tenant");
        var building = await CreateBuildingAsync(client, "Route Building");
        var unit = await CreateUnitAsync(client, building.Id, "101");
        var source = await CreateLeaseDraftAsync(client, unit.Id, party.Id, "2026-02-01", memo: "source");

        var deriveResponse = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}/derive",
            new
            {
                sourceDocumentId = source.Id,
                relationshipType = "based_on",
                initialPayload = new
                {
                    fields = new
                    {
                        property_id = unit.Id,
                        start_on_utc = "2026-03-01",
                        rent_amount = 1100.00m,
                        memo = "derived"
                    },
                    parts = new
                    {
                        parties = new
                        {
                            rows = new object[]
                            {
                                new
                                {
                                    party_id = party.Id,
                                    role = "PrimaryTenant",
                                    is_primary = true,
                                    ordinal = 1
                                }
                            }
                        }
                    }
                }
            });

        deriveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var derived = await deriveResponse.Content.ReadFromJsonAsync<DocumentDto>(Json);
        derived.Should().NotBeNull();
        derived!.Id.Should().NotBe(source.Id);
        derived.Status.Should().Be(DocumentStatus.Draft);
        derived.Payload.Fields!["start_on_utc"].GetString().Should().Be("2026-03-01");
        derived.Payload.Fields["memo"].GetString().Should().Be("derived");
    }

    [Fact]
    public async Task Post_Repost_Unpost_Marking_And_Delete_Routes_Work_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateHttpsClient(factory);

        var party = await CreatePartyAsync(client, "Lifecycle Tenant");
        var building = await CreateBuildingAsync(client, "Lifecycle Building");
        var unit = await CreateUnitAsync(client, building.Id, "202");
        var lease = await CreateLeaseDraftAsync(client, unit.Id, party.Id, "2026-04-01", memo: "route lifecycle");

        var postResponse = await client.PostAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}/post", content: null);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var posted = await postResponse.Content.ReadFromJsonAsync<DocumentDto>(Json);
        posted.Should().NotBeNull();
        posted!.Status.Should().Be(DocumentStatus.Posted);

        var repostResponse = await client.PostAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}/repost", content: null);
        repostResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reposted = await repostResponse.Content.ReadFromJsonAsync<DocumentDto>(Json);
        reposted.Should().NotBeNull();
        reposted!.Status.Should().Be(DocumentStatus.Posted);

        using (var postedMarkResponse = await client.PostAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}/mark-for-deletion", content: null))
        {
            postedMarkResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
            using var problem = await ReadJsonAsync(postedMarkResponse);
            problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("doc.workflow.state_mismatch");
        }

        using (var postedDeleteResponse = await client.DeleteAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}"))
        {
            postedDeleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
            using var problem = await ReadJsonAsync(postedDeleteResponse);
            problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("doc.workflow.state_mismatch");
        }

        var unpostResponse = await client.PostAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}/unpost", content: null);
        unpostResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unposted = await unpostResponse.Content.ReadFromJsonAsync<DocumentDto>(Json);
        unposted.Should().NotBeNull();
        unposted!.Status.Should().Be(DocumentStatus.Draft);

        var markResponse = await client.PostAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}/mark-for-deletion", content: null);
        markResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var marked = await markResponse.Content.ReadFromJsonAsync<DocumentDto>(Json);
        marked.Should().NotBeNull();
        marked!.IsMarkedForDeletion.Should().BeTrue();

        var unmarkResponse = await client.PostAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}/unmark-for-deletion", content: null);
        unmarkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unmarked = await unmarkResponse.Content.ReadFromJsonAsync<DocumentDto>(Json);
        unmarked.Should().NotBeNull();
        unmarked!.Status.Should().Be(DocumentStatus.Draft);
        unmarked.IsMarkedForDeletion.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Route_Removes_A_Regular_Draft_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateHttpsClient(factory);

        var party = await CreatePartyAsync(client, "Delete Tenant");
        var building = await CreateBuildingAsync(client, "Delete Building");
        var unit = await CreateUnitAsync(client, building.Id, "303");
        var lease = await CreateLeaseDraftAsync(client, unit.Id, party.Id, "2026-06-01", memo: "delete me");

        var deleteResponse = await client.DeleteAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var getAfterDeleteResponse = await client.GetAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}");
        getAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static HttpClient CreateHttpsClient(PmApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display,
                    is_tenant = true
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!;
    }

    private static async Task<CatalogItemDto> CreateBuildingAsync(HttpClient client, string display)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    display,
                    address_line1 = "123 Main St",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!;
    }

    private static async Task<CatalogItemDto> CreateUnitAsync(HttpClient client, Guid buildingId, string unitNo)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    parent_property_id = buildingId,
                    unit_no = unitNo
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!;
    }

    private static async Task<DocumentDto> CreateLeaseDraftAsync(HttpClient client, Guid propertyId, Guid partyId, string startOnUtc, string? memo)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = propertyId,
                    start_on_utc = startOnUtc,
                    rent_amount = 1000.00m,
                    memo
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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
