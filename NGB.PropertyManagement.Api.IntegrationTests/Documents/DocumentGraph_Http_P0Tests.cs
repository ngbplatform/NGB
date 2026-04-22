using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using NGB.Contracts.Graph;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class DocumentGraph_Http_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    // ASP.NET Core API is configured with JsonStringEnumConverter (see NGB.Api).
    // HttpClient JSON helpers use default options without enum-string support,
    // so tests must opt-in to the same enum serialization contract.
    private static readonly JsonSerializerOptions Json = CreateJson();

    public DocumentGraph_Http_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Graph_WhenDepth2_ReturnsNodesAndEdges()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // Create minimal refs for leases.
        var party = await CreateCatalogAsync(client, PropertyManagementCodes.Party, new { display = "Party" });
        var building = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new { kind = "Building", display = "Property", address_line1 = "Property", city = "Hoboken", state = "NJ", zip = "07030" });
        var unit = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new { kind = "Unit", parent_property_id = building.Id, unit_no = "1A" });

        var a = await CreateLeaseAsync(client, "Lease A", party.Id, unit.Id);
        var b = await CreateLeaseAsync(client, "Lease B", party.Id, unit.Id);
        var c = await CreateLeaseAsync(client, "Lease C", party.Id, unit.Id);

        await InsertRelationshipAsync(_fixture.ConnectionString, from: a.Id, to: b.Id, code: "based_on");
        await InsertRelationshipAsync(_fixture.ConnectionString, from: b.Id, to: c.Id, code: "created_from");

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.Lease}/{a.Id}/graph?depth=2&maxNodes=200");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var graph = await resp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
        graph.Should().NotBeNull();

        graph!.Nodes.Select(n => n.EntityId).Should().BeEquivalentTo([a.Id, b.Id, c.Id]);
        graph.Edges.Should().HaveCount(2);

        graph.Nodes.Should().OnlyContain(n => n.Kind == NGB.Contracts.Metadata.EntityKind.Document);
        graph.Nodes.Should().OnlyContain(n => n.TypeCode == PropertyManagementCodes.Lease);
        graph.Nodes.Should().OnlyContain(n => n.Depth >= 0);
        graph.Nodes.Should().OnlyContain(n => n.Amount == 1000.00m);

        graph.Edges.Should().Contain(e =>
            e.FromNodeId == NodeId(PropertyManagementCodes.Lease, a.Id)
            && e.ToNodeId == NodeId(PropertyManagementCodes.Lease, b.Id)
            && e.RelationshipType == "based_on");

        graph.Edges.Should().Contain(e =>
            e.FromNodeId == NodeId(PropertyManagementCodes.Lease, b.Id)
            && e.ToNodeId == NodeId(PropertyManagementCodes.Lease, c.Id)
            && e.RelationshipType == "created_from");
    }

    [Fact]
    public async Task Graph_WhenDepth1_DoesNotReachSecondHop()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreateCatalogAsync(client, PropertyManagementCodes.Party, new { display = "Party" });
        var building = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new { kind = "Building", display = "Property", address_line1 = "Property", city = "Hoboken", state = "NJ", zip = "07030" });
        var unit = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new { kind = "Unit", parent_property_id = building.Id, unit_no = "1A" });

        var a = await CreateLeaseAsync(client, "Lease A", party.Id, unit.Id);
        var b = await CreateLeaseAsync(client, "Lease B", party.Id, unit.Id);
        var c = await CreateLeaseAsync(client, "Lease C", party.Id, unit.Id);

        await InsertRelationshipAsync(_fixture.ConnectionString, from: a.Id, to: b.Id, code: "based_on");
        await InsertRelationshipAsync(_fixture.ConnectionString, from: b.Id, to: c.Id, code: "created_from");

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.Lease}/{a.Id}/graph?depth=1&maxNodes=200");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var graph = await resp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
        graph.Should().NotBeNull();

        graph!.Nodes.Select(n => n.EntityId).Should().BeEquivalentTo([a.Id, b.Id]);
        graph.Edges.Should().HaveCount(1);
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "based_on");
        graph.Nodes.Should().NotContain(n => n.EntityId == c.Id);
    }

    [Fact]
    public async Task Graph_WhenMaxNodes2_StopsAddingNodes_AndFiltersEdges()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreateCatalogAsync(client, PropertyManagementCodes.Party, new { display = "Party" });
        var building = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new { kind = "Building", display = "Property", address_line1 = "Property", city = "Hoboken", state = "NJ", zip = "07030" });
        var unit = await CreateCatalogAsync(client, PropertyManagementCodes.Property, new { kind = "Unit", parent_property_id = building.Id, unit_no = "1A" });

        var a = await CreateLeaseAsync(client, "Lease A", party.Id, unit.Id);
        var b = await CreateLeaseAsync(client, "Lease B", party.Id, unit.Id);
        var c = await CreateLeaseAsync(client, "Lease C", party.Id, unit.Id);

        await InsertRelationshipAsync(_fixture.ConnectionString, from: a.Id, to: b.Id, code: "based_on");
        await InsertRelationshipAsync(_fixture.ConnectionString, from: b.Id, to: c.Id, code: "created_from");

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.Lease}/{a.Id}/graph?depth=2&maxNodes=2");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var graph = await resp.Content.ReadFromJsonAsync<RelationshipGraphDto>(Json);
        graph.Should().NotBeNull();

        graph!.Nodes.Should().HaveCount(2);
        graph.Nodes.Select(n => n.EntityId).Should().Contain(a.Id);
        graph.Nodes.Select(n => n.EntityId).Should().Contain(b.Id);
        graph.Nodes.Should().NotContain(n => n.EntityId == c.Id);

        // Reader may temporarily collect edges to nodes that were not admitted due to MaxNodes,
        // but it must filter them out in the final result.
        graph.Edges.Should().HaveCount(1);
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "based_on");
        graph.Edges.Should().NotContain(e => e.RelationshipType == "created_from");
    }

    private static async Task<CatalogItemDto> CreateCatalogAsync(HttpClient client, string typeCode, object fields)
    {
        using var resp = await client.PostAsJsonAsync($"/api/catalogs/{typeCode}", new { fields });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static async Task<DocumentDto> CreateLeaseAsync(HttpClient client, string display, Guid partyId, Guid propertyId)
    {
        _ = display; // display is computed in DB for pm.lease

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
        dto!.Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Draft);
        return dto;
    }

    private static async Task InsertRelationshipAsync(string cs, Guid from, Guid to, string code)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code) VALUES ($1, $2, $3, $4);",
            conn);

        cmd.Parameters.AddWithValue(Guid.CreateVersion7());
        cmd.Parameters.AddWithValue(from);
        cmd.Parameters.AddWithValue(to);
        cmd.Parameters.AddWithValue(code);

        await cmd.ExecuteNonQueryAsync();
    }

    private static string NodeId(string typeCode, Guid id) => $"doc:{typeCode}:{id}";

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
