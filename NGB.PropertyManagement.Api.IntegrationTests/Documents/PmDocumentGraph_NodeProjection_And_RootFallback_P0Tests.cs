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
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmDocumentGraph_NodeProjection_And_RootFallback_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmDocumentGraph_NodeProjection_And_RootFallback_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Graph_ForIsolatedLease_ReturnsSingleRootNode_WithProjectedDisplayStatusAndAmount()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreateCatalogAsync(client, PropertyManagementCodes.Party, new { display = "Jamie Tenant", is_tenant = true, is_vendor = false });
        var building = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new
        {
            kind = "Building",
            display = "Atlas",
            address_line1 = "1 River St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        });
        var unit = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "5A"
        });

        var lease = await CreateLeaseAsync(client, party.Id, unit.Id);

        using var graphHttp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.Lease}/{lease.Id}/graph?depth=2&maxNodes=50");
        graphHttp.StatusCode.Should().Be(HttpStatusCode.OK);

        var graph = await graphHttp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
        graph.Should().NotBeNull();
        graph!.Edges.Should().BeEmpty();
        graph.Nodes.Should().ContainSingle();

        var root = graph.Nodes.Single();
        root.NodeId.Should().Be(NodeId(PropertyManagementCodes.Lease, lease.Id));
        root.Kind.Should().Be(EntityKind.Document);
        root.TypeCode.Should().Be(PropertyManagementCodes.Lease);
        root.EntityId.Should().Be(lease.Id);
        root.Title.Should().Be(lease.Display);
        root.DocumentStatus.Should().Be(DocumentStatus.Draft);
        root.Amount.Should().Be(1000.00m);
        root.Depth.Should().Be(0);
    }

    [Fact]
    public async Task Graph_ForMirroredMaintenanceChain_ProjectsCurrentDisplayAndStatus_WithoutDuplicateNodesOrEdges()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        await using var scope = factory.Services.CreateAsyncScope();

        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

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
        graph!.Nodes.Should().HaveCount(3);
        graph.Edges.Should().HaveCount(2);

        graph.Nodes.Select(n => n.NodeId).Should().OnlyHaveUniqueItems();
        graph.Nodes.Select(n => n.EntityId).Should().OnlyHaveUniqueItems();
        graph.Edges.Select(e => (e.FromNodeId, e.ToNodeId, e.RelationshipType)).Should().OnlyHaveUniqueItems();

        var byId = graph.Nodes.ToDictionary(n => n.EntityId);
        byId[completion.Id].Title.Should().Be(completion.Display);
        byId[completion.Id].DocumentStatus.Should().Be(DocumentStatus.Draft);
        byId[workOrder.Id].Title.Should().Be(workOrder.Display);
        byId[workOrder.Id].DocumentStatus.Should().Be(DocumentStatus.Posted);
        byId[request.Id].Title.Should().Be(request.Display);
        byId[request.Id].DocumentStatus.Should().Be(DocumentStatus.Posted);

        graph.Edges.Should().Contain(e =>
            e.FromNodeId == NodeId(PropertyManagementCodes.WorkOrderCompletion, completion.Id)
            && e.ToNodeId == NodeId(PropertyManagementCodes.WorkOrder, workOrder.Id)
            && e.RelationshipType == "created_from");
        graph.Edges.Should().Contain(e =>
            e.FromNodeId == NodeId(PropertyManagementCodes.WorkOrder, workOrder.Id)
            && e.ToNodeId == NodeId(PropertyManagementCodes.MaintenanceRequest, request.Id)
            && e.RelationshipType == "created_from");
    }

    private static async Task<CatalogItemDto> CreateCatalogAsync(HttpClient client, string typeCode, object fields)
    {
        using var resp = await client.PostAsJsonAsync($"/api/catalogs/{typeCode}", new { fields });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static async Task<DocumentDto> CreateLeaseAsync(HttpClient client, Guid partyId, Guid propertyId)
    {
        using var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = propertyId,
                    start_on_utc = "2026-02-01",
                    rent_amount = "1000.00"
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
        var dto = await resp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
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
