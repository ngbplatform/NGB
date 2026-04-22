using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMaintenanceRequest_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmMaintenanceRequest_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_Create_Update_Search_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreatePartyAsync(client, "John Resident");
        var property = await CreateUnitPropertyAsync(client, "101 Main St", "101");
        var category = await CreateMaintenanceCategoryAsync(client, "Plumbing");

        using (var metaResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.MaintenanceRequest}/metadata"))
        {
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await metaResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
            meta.Should().NotBeNull();
            meta!.DocumentType.Should().Be(PropertyManagementCodes.MaintenanceRequest);
            meta.List!.Columns.Should().Contain(c => c.Key == "subject" && c.Label == "Subject");
            meta.List!.Columns.Should().Contain(c => c.Key == "priority" && c.Label == "Priority");
            meta.List!.Columns.Should().Contain(c => c.Key == "requested_at_utc" && c.Label == "Requested At");
            meta.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields)
                .Should().Contain(f => f.Key == "category_id" && f.Label == "Category");
        }

        using var createResp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.MaintenanceRequest}",
            new
            {
                fields = new
                {
                    property_id = property.Id,
                    party_id = party.Id,
                    category_id = category.Id,
                    priority = "normal",
                    subject = "Kitchen sink leak",
                    description = "Water under the sink",
                    requested_at_utc = "2026-03-10"
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        created.Should().NotBeNull();
        created!.Status.Should().Be(DocumentStatus.Draft);
        created.Number.Should().StartWith("MR-");
        created.Payload.Fields!["priority"].GetString().Should().Be("Normal");
        created.Payload.Fields!["subject"].GetString().Should().Be("Kitchen sink leak");
        created.Payload.Fields!["display"].GetString().Should().Contain("Maintenance Request");
        created.Payload.Fields!["display"].GetString().Should().Contain(created.Number!);

        using var updateResp = await client.PutAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.MaintenanceRequest}/{created.Id}",
            new
            {
                fields = new
                {
                    property_id = property.Id,
                    party_id = party.Id,
                    category_id = category.Id,
                    priority = "high",
                    subject = "Kitchen sink leak - urgent",
                    description = "Water under the sink is getting worse",
                    requested_at_utc = "2026-03-10"
                }
            });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        updated.Should().NotBeNull();
        updated!.Payload.Fields!["priority"].GetString().Should().Be("High");
        updated.Payload.Fields!["subject"].GetString().Should().Be("Kitchen sink leak - urgent");

        var page = await client.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            $"/api/documents/{PropertyManagementCodes.MaintenanceRequest}?search={Uri.EscapeDataString(created.Number!)}&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().Contain(i => i.Id == created.Id);
    }

    [Fact]
    public async Task Metadata_And_PageFilter_ByLease_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var partyA = await catalogs.CreateAsync(
            PropertyManagementCodes.Party,
            Payload(new { display = "Lease Filter Party A", is_tenant = true, is_vendor = false }),
            CancellationToken.None);
        var partyB = await catalogs.CreateAsync(
            PropertyManagementCodes.Party,
            Payload(new { display = "Lease Filter Party B", is_tenant = true, is_vendor = false }),
            CancellationToken.None);

        var buildingA = await catalogs.CreateAsync(
            PropertyManagementCodes.Property,
            Payload(new { kind = "Building", display = "Bldg A", address_line1 = "201 Main St", city = "Hoboken", state = "NJ", zip = "07030" }),
            CancellationToken.None);
        var propertyA = await catalogs.CreateAsync(
            PropertyManagementCodes.Property,
            Payload(new { kind = "Unit", parent_property_id = buildingA.Id, unit_no = "201" }),
            CancellationToken.None);

        var buildingB = await catalogs.CreateAsync(
            PropertyManagementCodes.Property,
            Payload(new { kind = "Building", display = "Bldg B", address_line1 = "202 Main St", city = "Hoboken", state = "NJ", zip = "07030" }),
            CancellationToken.None);
        var propertyB = await catalogs.CreateAsync(
            PropertyManagementCodes.Property,
            Payload(new { kind = "Unit", parent_property_id = buildingB.Id, unit_no = "202" }),
            CancellationToken.None);

        var category = await catalogs.CreateAsync(
            PropertyManagementCodes.MaintenanceCategory,
            Payload(new { display = "Electrical" }),
            CancellationToken.None);

        var leaseA = await documents.CreateDraftAsync(
            PropertyManagementCodes.Lease,
            Payload(
                new
                {
                    property_id = propertyA.Id,
                    start_on_utc = "2026-03-01",
                    end_on_utc = "2026-12-31",
                    rent_amount = "1200.00"
                },
                LeaseParts.PrimaryTenant(partyA.Id)),
            CancellationToken.None);
        var leaseB = await documents.CreateDraftAsync(
            PropertyManagementCodes.Lease,
            Payload(
                new
                {
                    property_id = propertyB.Id,
                    start_on_utc = "2026-03-01",
                    end_on_utc = "2026-12-31",
                    rent_amount = "1350.00"
                },
                LeaseParts.PrimaryTenant(partyB.Id)),
            CancellationToken.None);

        var requestA = await documents.CreateDraftAsync(
            PropertyManagementCodes.MaintenanceRequest,
            Payload(new
            {
                property_id = propertyA.Id,
                party_id = partyA.Id,
                category_id = category.Id,
                priority = "normal",
                subject = "Lease A issue",
                description = "Lease A issue details",
                requested_at_utc = "2026-03-10"
            }),
            CancellationToken.None);
        var requestB = await documents.CreateDraftAsync(
            PropertyManagementCodes.MaintenanceRequest,
            Payload(new
            {
                property_id = propertyB.Id,
                party_id = partyB.Id,
                category_id = category.Id,
                priority = "normal",
                subject = "Lease B issue",
                description = "Lease B issue details",
                requested_at_utc = "2026-03-10"
            }),
            CancellationToken.None);

        var meta = await client.GetFromJsonAsync<DocumentTypeMetadataDto>(
            $"/api/documents/{PropertyManagementCodes.MaintenanceRequest}/metadata",
            Json);

        meta.Should().NotBeNull();
        var leaseFilter = meta!.List!.Filters!.Single(x => x.Key == "lease_id");
        leaseFilter.Label.Should().Be("Lease");
        leaseFilter.IsMulti.Should().BeTrue();
        leaseFilter.Lookup.Should().BeOfType<DocumentLookupSourceDto>()
            .Which.DocumentTypes.Should().Equal(PropertyManagementCodes.Lease);

        var filtered = await client.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            $"/api/documents/{PropertyManagementCodes.MaintenanceRequest}?lease_id={leaseA.Id}&offset=0&limit=50",
            Json);

        filtered.Should().NotBeNull();
        filtered!.Items.Should().Contain(x => x.Id == requestA.Id);
        filtered.Items.Should().NotContain(x => x.Id == requestB.Id);
        filtered.Items.Should().NotContain(x => x.Id == leaseA.Id || x.Id == leaseB.Id);
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display)
    {
        using var resp = await client.PostAsJsonAsync($"/api/catalogs/{PropertyManagementCodes.Party}", new
        {
            fields = new { display, is_tenant = true, is_vendor = false }
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!;
    }

    private static async Task<CatalogItemDto> CreateUnitPropertyAsync(HttpClient client, string addressLine1, string unitNo)
    {
        using var buildingResp = await client.PostAsJsonAsync($"/api/catalogs/{PropertyManagementCodes.Property}", new
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
        buildingResp.EnsureSuccessStatusCode();
        var building = (await buildingResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!;

        using var unitResp = await client.PostAsJsonAsync($"/api/catalogs/{PropertyManagementCodes.Property}", new
        {
            fields = new
            {
                kind = "Unit",
                parent_property_id = building.Id,
                unit_no = unitNo
            }
        });
        unitResp.EnsureSuccessStatusCode();
        return (await unitResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!;
    }

    private static async Task<CatalogItemDto> CreateMaintenanceCategoryAsync(HttpClient client, string display)
    {
        using var resp = await client.PostAsJsonAsync($"/api/catalogs/{PropertyManagementCodes.MaintenanceCategory}", new
        {
            fields = new { display }
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!;
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            values[property.Name] = property.Value;
        return new RecordPayload(values, parts);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
