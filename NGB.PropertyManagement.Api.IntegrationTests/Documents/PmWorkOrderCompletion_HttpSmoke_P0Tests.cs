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
public sealed class PmWorkOrderCompletion_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmWorkOrderCompletion_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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

        var (request, workOrder) = await CreatePrerequisitesAsync(catalogs, documents);

        using (var metaResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.WorkOrderCompletion}/metadata"))
        {
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await metaResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
            meta.Should().NotBeNull();
            meta!.DocumentType.Should().Be(PropertyManagementCodes.WorkOrderCompletion);
            meta.List!.Columns.Should().Contain(c => c.Key == "work_order_id" && c.Label == "Work Order");
            meta.List!.Columns.Should().Contain(c => c.Key == "closed_at_utc" && c.Label == "Closed At");
            meta.List!.Columns.Should().Contain(c => c.Key == "outcome" && c.Label == "Outcome");
            meta.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields)
                .Should().Contain(f => f.Key == "resolution_notes" && f.Label == "Resolution Notes");
            meta.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields)
                .Should().Contain(f => f.Key == "work_order_id"
                    && f.MirroredRelationship != null
                    && f.MirroredRelationship.RelationshipCode == "created_from");
        }

        using var createResp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.WorkOrderCompletion}",
            new
            {
                fields = new
                {
                    work_order_id = workOrder.Id,
                    closed_at_utc = "2026-03-13",
                    outcome = "completed",
                    resolution_notes = "Leak fixed and drain tested"
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        created.Should().NotBeNull();
        created!.Status.Should().Be(DocumentStatus.Draft);
        created.Number.Should().StartWith("WOC-");
        created.Payload.Fields!["outcome"].GetString().Should().Be("Completed");
        created.Payload.Fields!["display"].GetString().Should().Contain("Work Order Completion");
        created.Payload.Fields!["display"].GetString().Should().Contain(created.Number!);

        using var updateResp = await client.PutAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.WorkOrderCompletion}/{created.Id}",
            new
            {
                fields = new
                {
                    work_order_id = workOrder.Id,
                    closed_at_utc = "2026-03-14",
                    outcome = "unable_to_complete",
                    resolution_notes = "Vendor could not access unit"
                }
            });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        updated.Should().NotBeNull();
        updated!.Payload.Fields!["outcome"].GetString().Should().Be("UnableToComplete");
        updated.Payload.Fields!["resolution_notes"].GetString().Should().Be("Vendor could not access unit");

        var page = await client.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            $"/api/documents/{PropertyManagementCodes.WorkOrderCompletion}?search={Uri.EscapeDataString(created.Number!)}&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().Contain(i => i.Id == created.Id);
    }

    private static async Task<(DocumentDto Request, DocumentDto WorkOrder)> CreatePrerequisitesAsync(ICatalogService catalogs, IDocumentService documents)
    {
        var resident = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "John Resident", is_tenant = true, is_vendor = false }), CancellationToken.None);
        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "FixIt Vendor", is_tenant = false, is_vendor = true }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "101 Main St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);
        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);
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

        var workOrder = await documents.CreateDraftAsync(
            PropertyManagementCodes.WorkOrder,
            Payload(new
            {
                request_id = request.Id,
                assigned_party_id = vendor.Id,
                scope_of_work = "Inspect leak and replace trap",
                due_by_utc = "2026-03-12",
                cost_responsibility = "owner"
            }),
            CancellationToken.None);

        workOrder = await documents.PostAsync(PropertyManagementCodes.WorkOrder, workOrder.Id, CancellationToken.None);
        return (request, workOrder);
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
