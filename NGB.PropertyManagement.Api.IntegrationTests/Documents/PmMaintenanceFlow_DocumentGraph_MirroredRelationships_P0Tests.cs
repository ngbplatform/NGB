using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Graph;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMaintenanceFlow_DocumentGraph_MirroredRelationships_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmMaintenanceFlow_DocumentGraph_MirroredRelationships_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Graph_ForWorkOrderCompletion_ExplainsMirroredCreatedFromChain()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        await using var scope = factory.Services.CreateAsyncScope();

        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var (request, workOrder) = await CreatePostedWorkOrderAsync(catalogs, documents);

        var completion = await documents.CreateDraftAsync(
            PropertyManagementCodes.WorkOrderCompletion,
            Payload(new
            {
                work_order_id = workOrder.Id,
                closed_at_utc = "2026-03-13",
                outcome = "completed",
                resolution_notes = "Leak fixed and drain tested"
            }),
            CancellationToken.None);

        using var graphHttp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.WorkOrderCompletion}/{completion.Id}/graph?depth=2&maxNodes=50");
        graphHttp.StatusCode.Should().Be(HttpStatusCode.OK);

        var graph = await graphHttp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
        graph.Should().NotBeNull();
        graph!.Nodes.Select(n => n.EntityId).Should().BeEquivalentTo([completion.Id, workOrder.Id, request.Id]);
        graph.Edges.Should().Contain(e =>
            e.FromNodeId == NodeId(PropertyManagementCodes.WorkOrderCompletion, completion.Id)
            && e.ToNodeId == NodeId(PropertyManagementCodes.WorkOrder, workOrder.Id)
            && e.RelationshipType == "created_from");
        graph.Edges.Should().Contain(e =>
            e.FromNodeId == NodeId(PropertyManagementCodes.WorkOrder, workOrder.Id)
            && e.ToNodeId == NodeId(PropertyManagementCodes.MaintenanceRequest, request.Id)
            && e.RelationshipType == "created_from");
    }

    [Fact]
    public async Task Graph_ForMaintenanceRequest_IncludesIncomingMirroredWorkOrderEdge()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        await using var scope = factory.Services.CreateAsyncScope();

        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var (request, workOrder) = await CreatePostedWorkOrderAsync(catalogs, documents);

        using var graphHttp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.MaintenanceRequest}/{request.Id}/graph?depth=1&maxNodes=50");
        graphHttp.StatusCode.Should().Be(HttpStatusCode.OK);

        var graph = await graphHttp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
        graph.Should().NotBeNull();
        graph!.Nodes.Select(n => n.EntityId).Should().BeEquivalentTo([request.Id, workOrder.Id]);
        graph.Edges.Should().ContainSingle(e =>
            e.FromNodeId == NodeId(PropertyManagementCodes.WorkOrder, workOrder.Id)
            && e.ToNodeId == NodeId(PropertyManagementCodes.MaintenanceRequest, request.Id)
            && e.RelationshipType == "created_from");
    }

    private static async Task<(DocumentDto Request, DocumentDto WorkOrder)> CreatePostedWorkOrderAsync(ICatalogService catalogs, IDocumentService documents)
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

    private static RecordPayload Payload(object obj)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict);
    }

    private static string NodeId(string typeCode, Guid id) => $"doc:{typeCode}:{id}";

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
