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
public sealed class PmWorkOrder_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmWorkOrder_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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
        await using var scope = factory.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var resident = await CreatePartyAsync(catalogs, "John Resident", isTenant: true, isVendor: false);
        var vendor = await CreatePartyAsync(catalogs, "FixIt Vendor", isTenant: false, isVendor: true);
        var property = await CreateUnitPropertyAsync(catalogs, "101 Main St", "101");
        var category = await catalogs.CreateAsync(PropertyManagementCodes.MaintenanceCategory, Payload(new { display = "Plumbing" }), CancellationToken.None);

        var request = await documents.CreateDraftAsync(
            PropertyManagementCodes.MaintenanceRequest,
            Payload(new
            {
                property_id = property.Id,
                party_id = resident.Id,
                category_id = category.Id,
                priority = "normal",
                subject = "Kitchen sink leak",
                description = "Water under the sink",
                requested_at_utc = "2026-03-10"
            }),
            CancellationToken.None);

        request = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, request.Id, CancellationToken.None);

        using (var metaResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.WorkOrder}/metadata"))
        {
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await metaResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
            meta.Should().NotBeNull();
            meta!.DocumentType.Should().Be(PropertyManagementCodes.WorkOrder);
            meta.List!.Columns.Should().Contain(c => c.Key == "request_id" && c.Label == "Request");
            meta.List!.Columns.Should().Contain(c => c.Key == "due_by_utc" && c.Label == "Due By");
            meta.List!.Columns.Should().Contain(c => c.Key == "cost_responsibility" && c.Label == "Cost Responsibility");
            meta.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields)
                .Should().Contain(f => f.Key == "assigned_party_id" && f.Label == "Assigned To");
            meta.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields)
                .Should().Contain(f => f.Key == "request_id"
                    && f.MirroredRelationship != null
                    && f.MirroredRelationship.RelationshipCode == "created_from");
        }

        using var createResp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.WorkOrder}",
            new
            {
                fields = new
                {
                    request_id = request.Id,
                    assigned_party_id = vendor.Id,
                    scope_of_work = "Inspect leak and replace trap",
                    due_by_utc = "2026-03-12",
                    cost_responsibility = "owner"
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        created.Should().NotBeNull();
        created!.Status.Should().Be(DocumentStatus.Draft);
        created.Number.Should().StartWith("WO-");
        created.Payload.Fields!["cost_responsibility"].GetString().Should().Be("Owner");
        created.Payload.Fields!["display"].GetString().Should().Contain("Work Order");
        created.Payload.Fields!["display"].GetString().Should().Contain(created.Number!);

        using var updateResp = await client.PutAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.WorkOrder}/{created.Id}",
            new
            {
                fields = new
                {
                    request_id = request.Id,
                    assigned_party_id = vendor.Id,
                    scope_of_work = "Inspect leak, replace trap, test drain",
                    due_by_utc = "2026-03-13",
                    cost_responsibility = "tenant"
                }
            });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        updated.Should().NotBeNull();
        updated!.Payload.Fields!["scope_of_work"].GetString().Should().Be("Inspect leak, replace trap, test drain");
        updated.Payload.Fields!["cost_responsibility"].GetString().Should().Be("Tenant");

        var page = await client.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            $"/api/documents/{PropertyManagementCodes.WorkOrder}?search={Uri.EscapeDataString(created.Number!)}&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().Contain(i => i.Id == created.Id);
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(ICatalogService catalogs, string display, bool isTenant, bool isVendor)
        => await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display, is_tenant = isTenant, is_vendor = isVendor }), CancellationToken.None);

    private static async Task<CatalogItemDto> CreateUnitPropertyAsync(ICatalogService catalogs, string addressLine1, string unitNo)
    {
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = addressLine1,
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        return await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = unitNo
        }), CancellationToken.None);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
