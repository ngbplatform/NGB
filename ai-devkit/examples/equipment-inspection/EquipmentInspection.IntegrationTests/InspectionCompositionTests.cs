using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Graph;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Security;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Persistence.Security;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.Documents;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionCompositionTests
{
    private const string DocumentsPath = "/api/documents/" + EquipmentDefinitions.InspectionCode;

    [Fact]
    public async Task Draft_numbers_are_unique_under_concurrency_and_survive_failed_post_and_lifecycle_changes()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var drafts = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            PostAsync<DocumentDto>(admin, DocumentsPath, Payload(("display", $"Numbered inspection {index}")))));
        Assert.Equal(8, drafts.Select(draft => draft.Number).Distinct().Count());
        Assert.All(drafts, draft => Assert.Matches(@"^I-\d{4}-\d{6}$", draft.Number!));
        var draft = drafts[0];
        var path = DocumentsPath + "/" + draft.Id;
        var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        using var invalid = await ExecutePostAsync(admin, path, state!.DocumentVersion, "incomplete-numbered-inspection");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(draft.Number, (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!.Number);
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Numbering bench")));
        using var completed = await admin.PutAsJsonAsync(path, Payload(("equipment_id", equipment.Id), ("result", "Passed")));
        await EnsureSuccessAsync(completed);
        state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        foreach (var action in new[] { "post", "repost", "unpost", "post" })
        {
            using var response = await ExecuteActionAsync(admin, path, action, state!.DocumentVersion,
                action + "-" + state.DocumentVersion);
            var result = await ReadActionAsync(response);
            Assert.Equal(draft.Number, result.Document.Number);
            state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        }
        Assert.Equal(drafts.Skip(1).Select(item => item.Number).Order(),
            (await Task.WhenAll(drafts.Skip(1).Select(item => admin.GetFromJsonAsync<DocumentDto>(DocumentsPath + "/" + item.Id, Json))))
                .Select(item => item!.Number).Order());
    }

    [Fact]
    public async Task Document_pages_bound_search_and_cursor_reads_without_duplicates_or_unauthorized_access()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var expected = new List<Guid>();
        foreach (var label in new[] { "Scheduled Alpha", "Scheduled Bravo", "Scheduled Charlie" })
        {
            var document = await PostAsync<DocumentDto>(admin, DocumentsPath, Payload(("display", label)));
            expected.Add(document.Id);
        }
        await PostAsync<DocumentDto>(admin, DocumentsPath, Payload(("display", "Unrelated inspection")));
        var path = DocumentsPath + "?search=Scheduled&limit=2&includeTotal=true";
        var first = (await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(path, Json))!;
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(3, first.Total);
        Assert.True(first.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(first.NextCursor));
        var second = (await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            path + "&cursor=" + Uri.EscapeDataString(first.NextCursor!), Json))!;
        Assert.Single(second.Items);
        Assert.False(second.HasMore);
        Assert.Null(second.NextCursor);
        Assert.Equal(expected.Order(), first.Items.Concat(second.Items).Select(item => item.Id).Order());
        var repeated = (await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(path, Json))!;
        Assert.Equal(first.Items.Select(item => item.Id), repeated.Items.Select(item => item.Id));
        var empty = (await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(DocumentsPath + "?search=Absent&limit=2", Json))!;
        Assert.Empty(empty.Items);
        using var malformed = await admin.GetAsync(path + "&cursor=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var forbidden = await reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Cross_type_lookup_bounds_each_type_and_filters_types_without_lookup_permission()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "example.inspection-viewer", "Inspection viewer");
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        string[] types = [EquipmentDefinitions.InspectionCode, EquipmentUsageDefinitions.DocumentCode];
        foreach (var type in types)
        {
            foreach (var label in new[] { "Lookup Alpha", "Lookup Bravo", "Lookup Charlie" })
                await PostAsync<DocumentDto>(admin, "/api/documents/" + type, Payload(("display", label)));
        }
        var request = new DocumentLookupAcrossTypesRequestDto(types, "Lookup", PerTypeLimit: 2);
        var all = await PostAsync<DocumentLookupDto[]>(admin, "/api/documents/lookup", request);
        Assert.Equal(4, all.Length);
        Assert.All(all.GroupBy(item => item.DocumentType), group => Assert.Equal(2, group.Count()));
        Assert.Equal(all.Select(item => item.Id),
            (await PostAsync<DocumentLookupDto[]>(admin, "/api/documents/lookup", request)).Select(item => item.Id));
        Assert.Empty(await PostAsync<DocumentLookupDto[]>(reader, "/api/documents/lookup", request));
        await GrantAsync(host, admin, NgbPermissionActions.View);
        Assert.Empty(await PostAsync<DocumentLookupDto[]>(reader, "/api/documents/lookup", request));
        await GrantAsync(host, admin, NgbPermissionActions.View, NgbPermissionActions.Lookup);
        var allowed = await PostAsync<DocumentLookupDto[]>(reader, "/api/documents/lookup", request);
        Assert.Equal(2, allowed.Length);
        Assert.All(allowed, item => Assert.Equal(EquipmentDefinitions.InspectionCode, item.DocumentType));
        Assert.Equal(all.Where(item => item.DocumentType == EquipmentDefinitions.InspectionCode).Select(item => item.Id),
            allowed.Select(item => item.Id));
        Assert.Empty(await PostAsync<DocumentLookupDto[]>(admin, "/api/documents/lookup", request with { PerTypeLimit = 0 }));
        Assert.Empty(await PostAsync<DocumentLookupDto[]>(admin, "/api/documents/lookup", request with { Query = "Absent" }));
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync("/api/documents/lookup", request);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Reinspection_derivation_creates_one_numbered_draft_and_a_directed_flow_edge_with_target_create_permission()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "example.inspection-viewer", "Inspection viewer");
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        await GrantAsync(host, admin, NgbPermissionActions.View);
        var (path, version) = await CreateCompleteInspectionAsync(admin);
        using var posted = await ExecutePostAsync(admin, path, version, "source-post");
        var source = await ReadActionAsync(posted);
        var readState = (await reader.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        Assert.DoesNotContain(readState.Actions, action => action.Code == InspectionDerivationDefinitions.ActionCode);
        using var denied = await ExecuteActionAsync(reader, path, InspectionDerivationDefinitions.ActionCode,
            source.DocumentVersion, "denied-reinspection");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var derived = await ExecuteActionAsync(admin, path, InspectionDerivationDefinitions.ActionCode,
            source.DocumentVersion, "new-reinspection");
        var result = await ReadActionAsync(derived);
        var child = Assert.IsType<DocumentDto>(result.CreatedDocument);
        Assert.Equal(NGB.Contracts.Metadata.DocumentStatus.Draft, child.Status);
        Assert.NotEqual(source.Document.Id, child.Id);
        Assert.NotEqual(source.Document.Number, child.Number);
        Assert.Matches(@"^I-\d{4}-\d{6}$", child.Number!);
        Assert.Equal(source.Document.Status, result.Document.Status);
        var childPath = DocumentsPath + "/" + child.Id;
        Assert.Equal(child.Id, (await admin.GetFromJsonAsync<DocumentDto>(childPath, Json))!.Id);
        await AssertNoEffectsAsync(admin, childPath);

        using var replay = await ExecuteActionAsync(admin, path, InspectionDerivationDefinitions.ActionCode,
            source.DocumentVersion, "new-reinspection");
        Assert.Equal(child.Id, (await ReadActionAsync(replay)).CreatedDocument!.Id);
        var page = (await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(DocumentsPath + "?includeTotal=true", Json))!;
        Assert.Equal(2, page.Total);
        using var deniedFlow = await reader.GetAsync(childPath + "/graph");
        Assert.Equal(HttpStatusCode.Forbidden, deniedFlow.StatusCode);
        await GrantAsync(host, admin, NgbPermissionActions.View, NgbPermissionActions.ViewFlow);
        var graph = (await reader.GetFromJsonAsync<RelationshipGraphDto>(childPath + "/graph?depth=2&maxNodes=10", Json))!;
        Assert.Equal(2, graph.Nodes.Count);
        var childNode = Assert.Single(graph.Nodes, node => node.EntityId == child.Id);
        var sourceNode = Assert.Single(graph.Nodes, node => node.EntityId == source.Document.Id);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(childNode.NodeId, edge.FromNodeId);
        Assert.Equal(sourceNode.NodeId, edge.ToNodeId);
        Assert.Equal(InspectionDerivationDefinitions.RelationshipCode, edge.RelationshipType);
        var bounded = (await admin.GetFromJsonAsync<RelationshipGraphDto>(childPath + "/graph?depth=1&maxNodes=1", Json))!;
        Assert.Single(bounded.Nodes);
        Assert.Empty(bounded.Edges);
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync(childPath + "/graph");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var other = await PostAsync<DocumentDto>(admin, DocumentsPath, Payload(("display", "Another source")));
        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        Assert.Equal((await documents.GetAsync(source.Document.Id))!.DateUtc,
            (await documents.GetAsync(child.Id))!.DateUtc);
        var relationships = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
        Assert.False(await relationships.CreateAsync(child.Id, source.Document.Id, InspectionDerivationDefinitions.RelationshipCode));
        await Assert.ThrowsAsync<DocumentRelationshipValidationException>(() =>
            relationships.CreateAsync(child.Id, other.Id, InspectionDerivationDefinitions.RelationshipCode));
        Assert.Single(await relationships.ListOutgoingAsync(child.Id));
        var usage = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode,
            Payload(("display", "Unrelated usage")));
        await Assert.ThrowsAsync<DocumentRelationshipValidationException>(() =>
            relationships.CreateAsync(other.Id, usage.Id, InspectionDerivationDefinitions.RelationshipCode));
        Assert.Empty(await relationships.ListOutgoingAsync(other.Id));
    }

    [Fact]
    public async Task Flow_omits_denied_types_and_their_edges_while_bidirectional_relationships_preserve_both_directions()
    {
        await using var host = new InspectionHost(configureServices: services =>
            services.AddSingleton<IDefinitionsContributor, RelatedUsageDefinitions>());
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "example.inspection-viewer", "Inspection viewer");
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        await GrantAsync(host, admin, NgbPermissionActions.View, NgbPermissionActions.ViewFlow);
        var inspection = await PostAsync<DocumentDto>(admin, DocumentsPath, Payload(("display", "Visible inspection")));
        var usage = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode,
            Payload(("display", "Restricted usage")));
        await using var scope = host.Services.CreateAsyncScope();
        var relationships = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
        Assert.True(await relationships.CreateAsync(inspection.Id, usage.Id, RelatedUsageDefinitions.RelationshipCode));
        Assert.False(await relationships.CreateAsync(inspection.Id, usage.Id, RelatedUsageDefinitions.RelationshipCode));
        Assert.Equal(usage.Id, Assert.Single(await relationships.ListOutgoingAsync(inspection.Id)).ToDocumentId);
        Assert.Equal(inspection.Id, Assert.Single(await relationships.ListOutgoingAsync(usage.Id)).ToDocumentId);
        var path = DocumentsPath + "/" + inspection.Id + "/graph?depth=2&maxNodes=10";
        var full = (await admin.GetFromJsonAsync<RelationshipGraphDto>(path, Json))!;
        Assert.Equal(2, full.Nodes.Count);
        Assert.Equal(2, full.Edges.Count);
        Assert.Equal(full.Edges[0].FromNodeId, full.Edges[1].ToNodeId);
        Assert.Equal(full.Edges[0].ToNodeId, full.Edges[1].FromNodeId);
        var filtered = (await reader.GetFromJsonAsync<RelationshipGraphDto>(path, Json))!;
        Assert.Equal(inspection.Id, Assert.Single(filtered.Nodes).EntityId);
        Assert.Empty(filtered.Edges);
        Assert.DoesNotContain("Restricted usage", await reader.GetStringAsync(path));
    }

    [Fact]
    public async Task Invalid_mirrored_relationship_metadata_prevents_host_startup()
    {
        await using var host = new InspectionHost(configureServices: services =>
            services.AddSingleton<IDefinitionsContributor, InvalidMirroredRelationshipDefinitions>());
        var error = await Assert.ThrowsAsync<DefinitionsValidationException>(host.StartAsync);
        Assert.Contains(error.Errors, message => message.Contains("source_id", StringComparison.Ordinal)
            && message.Contains("must use ColumnType.Guid", StringComparison.Ordinal));
    }

    private static async Task GrantAsync(InspectionHost host, HttpClient admin, params string[] actions)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var role = await scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>().GetByCodeAsync("example.inspection-viewer");
        using var response = await admin.PutAsJsonAsync($"/api/security/roles/{role!.RoleId}/permissions",
            new ReplaceRolePermissionsRequestDto(actions.Select(action =>
                new PermissionAssignmentDto(NgbResourceKinds.Document, EquipmentDefinitions.InspectionCode, action)).ToArray()));
        await EnsureSuccessAsync(response);
    }

    private sealed class RelatedUsageDefinitions : IDefinitionsContributor
    {
        public const string RelationshipCode = "example.related-usage";

        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocumentRelationshipType(RelationshipCode, relationship => relationship
                .Name("Related usage")
                .ManyToMany()
                .Bidirectional()
                .AllowFromDocumentTypes(EquipmentDefinitions.InspectionCode, EquipmentUsageDefinitions.DocumentCode)
                .AllowToDocumentTypes(EquipmentDefinitions.InspectionCode, EquipmentUsageDefinitions.DocumentCode));
        }
    }

    private sealed class InvalidMirroredRelationshipDefinitions : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            const string typeCode = "example.invalid-mirror";
            const string relationshipCode = "example.invalid-source";
            builder.AddDocumentRelationshipType(relationshipCode, relationship => relationship
                .Name("Inspection source")
                .ManyToOne()
                .AllowFromDocumentTypes(typeCode)
                .AllowToDocumentTypes(EquipmentDefinitions.InspectionCode));
            builder.AddDocument(typeCode, document => document.Metadata(new DocumentTypeMetadata(
                TypeCode: typeCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "doc_example_invalid_mirror",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("document_id", ColumnType.Guid, Required: true),
                            new("display", ColumnType.String),
                            new("source_id", ColumnType.String,
                                Lookup: new DocumentLookupSourceMetadata([EquipmentDefinitions.InspectionCode]),
                                MirroredRelationship: new MirroredDocumentRelationshipMetadata(relationshipCode))
                        ])
                ])));
        }
    }
}
