using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionSourceTests
{
    private const string Documents = "/api/documents/example.inspection";

    [Fact]
    public async Task Search_filters_before_paging_and_preview_rechecks_status_scope_and_permission()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        using var anonymous = host.CreateAnonymousClient();
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Source search")));
        var draft = await CreateAsync(admin, equipment.Id);
        var first = await ConfirmAsync(admin, await CreateAsync(admin, equipment.Id));
        var second = await ConfirmAsync(admin, await CreateAsync(admin, equipment.Id));
        var search = $"/api/inspection-sources?equipmentId={equipment.Id}";
        var all = (await admin.GetFromJsonAsync<InspectionSource[]>(search, Json))!;
        Assert.Equal(2, all.Length);
        Assert.DoesNotContain(all, item => item.Id == draft.Id);
        Assert.Contains(all, item => item.Id == first.Id);
        Assert.Contains(all, item => item.Id == second.Id);
        Assert.Equal(all.Take(1), await admin.GetFromJsonAsync<InspectionSource[]>(search + "&limit=1", Json));
        Assert.Equal(all.Skip(1), await admin.GetFromJsonAsync<InspectionSource[]>(search + "&offset=1&limit=1", Json));
        Assert.Empty((await admin.GetFromJsonAsync<InspectionSource[]>(search + "&query=missing", Json))!);
        Assert.Single((await admin.GetFromJsonAsync<InspectionSource[]>(search + "&query=" + first.Number, Json))!);
        Assert.Equal(first.Id, (await admin.GetFromJsonAsync<InspectionSource>(
            $"/api/inspection-sources/{first.Id}?equipmentId={equipment.Id}", Json))!.Id);

        foreach (var path in new[] { search + "&limit=101", search + "&offset=-1", "/api/inspection-sources" })
        {
            using var invalid = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (var id in new[] { draft.Id, Guid.NewGuid() })
        {
            using var unavailable = await admin.GetAsync($"/api/inspection-sources/{id}?equipmentId={equipment.Id}");
            await AssertErrorAsync(unavailable, HttpStatusCode.Conflict, "example.inspection.source_unavailable");
        }
        using var wrongEquipment = await admin.GetAsync($"/api/inspection-sources/{first.Id}?equipmentId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Conflict, wrongEquipment.StatusCode);
        foreach (var path in new[] { search, $"/api/inspection-sources/{first.Id}?equipmentId={equipment.Id}" })
        {
            using var forbidden = await reader.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var denied = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
    }

    [Fact]
    public async Task Competing_confirmations_consume_one_source_and_unpost_restores_eligibility()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Reinspection race")));
        var source = await ConfirmAsync(admin, await CreateAsync(admin, equipment.Id));
        var first = await DeriveAsync(admin, source, equipment.Id);
        var second = await DeriveAsync(admin, source, equipment.Id);
        var responses = await Task.WhenAll(
            ExecutePostAsync(admin, Documents + "/" + first.Id, await VersionAsync(admin, first.Id), "first-reinspection"),
            ExecutePostAsync(admin, Documents + "/" + second.Id, await VersionAsync(admin, second.Id), "second-reinspection"));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var winner = await ReadActionAsync(Assert.Single(responses, item => item.IsSuccessStatusCode));
        await AssertErrorAsync(Assert.Single(responses, item => !item.IsSuccessStatusCode),
            HttpStatusCode.Conflict, "example.inspection.source_unavailable");
        var lookup = $"/api/inspection-sources?equipmentId={equipment.Id}";
        Assert.DoesNotContain((await admin.GetFromJsonAsync<InspectionSource[]>(lookup, Json))!, item => item.Id == source.Id);
        using var stalePreview = await admin.GetAsync($"/api/inspection-sources/{source.Id}?equipmentId={equipment.Id}");
        Assert.Equal(HttpStatusCode.Conflict, stalePreview.StatusCode);
        var winnerPath = Documents + "/" + winner.Document.Id;
        using var repost = await ExecuteActionAsync(admin, winnerPath, "repost", winner.DocumentVersion, "own-source-repost");
        var reposted = await ReadActionAsync(repost);
        using var unpost = await ExecuteActionAsync(admin, winnerPath, "unpost", reposted.DocumentVersion, "release-source");
        await ReadActionAsync(unpost);
        Assert.Contains((await admin.GetFromJsonAsync<InspectionSource[]>(lookup, Json))!, item => item.Id == source.Id);
    }

    private static async Task<long> VersionAsync(HttpClient client, Guid id)
        => (await client.GetFromJsonAsync<DocumentEditorStateDto>(Documents + "/" + id + "/editor-state", Json))!.DocumentVersion;

    private static Task<DocumentDto> CreateAsync(HttpClient client, Guid equipmentId)
        => InspectionRequests.PostAsync<DocumentDto>(client, Documents, CompleteInspectionPayload(equipmentId));

    private static async Task<DocumentDto> ConfirmAsync(HttpClient client, DocumentDto draft)
    {
        using var response = await ExecutePostAsync(client, Documents + "/" + draft.Id, await VersionAsync(client, draft.Id), Guid.NewGuid().ToString());
        return (await ReadActionAsync(response)).Document;
    }

    private static async Task<DocumentDto> DeriveAsync(HttpClient client, DocumentDto source, Guid equipmentId)
    {
        using var response = await ExecuteActionAsync(client, Documents + "/" + source.Id,
            InspectionDerivationDefinitions.ActionCode, await VersionAsync(client, source.Id), Guid.NewGuid().ToString());
        var draft = (await ReadActionAsync(response)).CreatedDocument!;
        using var save = await client.PutAsJsonAsync(Documents + "/" + draft.Id, CompleteInspectionPayload(equipmentId));
        await EnsureSuccessAsync(save);
        return (await save.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
    }
}
