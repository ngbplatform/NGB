using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Documents;
using NGB.Contracts.Graph;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class DocumentRelationshipBatchTests
{
    private const string Relationship = InspectionDerivationDefinitions.RelationshipCode;

    [Fact]
    public async Task Batch_relationships_preserve_atomicity_idempotency_cardinality_and_authorized_flow()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var ids = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            var document = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentDefinitions.InspectionCode,
                Payload(("display", "Related inspection " + index)));
            ids.Add(document.Id);
        }
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var relationships = Assert.IsAssignableFrom<IDocumentRelationshipBatchService>(
            services.GetRequiredService<IDocumentRelationshipService>());
        var uow = services.GetRequiredService<IUnitOfWork>();
        var audit = services.GetRequiredService<IAuditEventReader>();
        DocumentRelationshipCreateRequest[] requests = [new(ids[0], ids[1], Relationship), new(ids[2], ids[1], Relationship)];

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInUowTransactionAsync(async ct =>
        {
            Assert.Equal(2, await relationships.CreateManyAsync(requests, manageTransaction: false, ct: ct));
            throw new InvalidOperationException("Deliberate caller failure after relationship writes.");
        }));
        Assert.Empty(await relationships.ListIncomingAsync(ids[1]));
        Assert.Empty(await audit.QueryAsync(new AuditLogQuery(ActionCode: "document.relationship.create")));

        Assert.Equal(2, await relationships.CreateManyAsync([requests[0], requests[1], requests[0]]));
        Assert.Equal(0, await relationships.CreateManyAsync(requests));
        var saved = await relationships.ListIncomingAsync(ids[1]);
        Assert.Equal(ids.Take(3).Where(id => id != ids[1]).Order(), saved.Select(row => row.FromDocumentId).Order());
        var before = await audit.QueryAsync(new AuditLogQuery(EntityId: saved[0].Id));
        Assert.Single(before);
        Assert.Equal(0, await relationships.CreateManyAsync(requests));
        Assert.Single(await audit.QueryAsync(new AuditLogQuery(EntityId: saved[0].Id)));

        await Assert.ThrowsAsync<DocumentRelationshipValidationException>(() => relationships.CreateManyAsync(
            [new(ids[3], ids[2], Relationship), new(ids[0], ids[3], Relationship)]));
        Assert.Empty(await relationships.ListOutgoingAsync(ids[3]));
        Assert.Single(await relationships.ListOutgoingAsync(ids[0]));
        var endpoint = "/api/documents/" + EquipmentDefinitions.InspectionCode + "/" + ids[1] + "/graph?depth=2&maxNodes=10";
        var graph = await admin.GetFromJsonAsync<RelationshipGraphDto>(endpoint, Json);
        Assert.Equal(3, graph!.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count);
        using var reader = await host.CreateClientAsync("reader");
        using var denied = await reader.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Batch_cycles_limits_and_cancellation_leave_no_partial_relationships()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var drafts = Assert.IsAssignableFrom<IDocumentDraftBatchService>(services.GetRequiredService<IDocumentDraftService>());
        var ids = await drafts.CreateDraftsAsync(Enumerable.Range(0, 3)
            .Select(_ => new DocumentDraftCreateRequest(EquipmentDefinitions.InspectionCode, null, DateTime.UtcNow)).ToArray());
        var relationships = Assert.IsAssignableFrom<IDocumentRelationshipBatchService>(
            services.GetRequiredService<IDocumentRelationshipService>());
        Assert.Equal(0, await relationships.CreateManyAsync([]));
        await Assert.ThrowsAsync<DocumentRelationshipValidationException>(() => relationships.CreateManyAsync(
            [new(ids[0], ids[1], Relationship), new(ids[1], ids[2], Relationship), new(ids[2], ids[0], Relationship)]));
        var request = new DocumentRelationshipCreateRequest(ids[0], ids[1], Relationship);
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() =>
            relationships.CreateManyAsync(Enumerable.Repeat(request, 1001).ToArray()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            relationships.CreateManyAsync([request], ct: cancellation.Token));
        foreach (var id in ids)
        {
            Assert.Empty(await relationships.ListOutgoingAsync(id));
        }
        Assert.False(services.GetRequiredService<IUnitOfWork>().HasActiveTransaction);
    }
}
