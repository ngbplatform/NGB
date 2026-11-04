using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class DocumentChangeAcceptance
{
    [Fact]
    public async Task Draft_preserves_missing_empty_and_whitespace_completion_notes()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection", Payload());
        Assert.Equal(DocumentStatus.Draft, draft.Status);
        var path = $"/api/documents/example.inspection/{draft.Id}";
        foreach (var note in new[] { "", " \t " })
        {
            using var update = await admin.PutAsJsonAsync(path, Payload(("completion_note", note)));
            await EnsureSuccessAsync(update);
            var saved = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
            Assert.Equal(DocumentStatus.Draft, saved!.Status);
            Assert.Equal(note, saved.Payload.Fields!["completion_note"].GetString());
        }
    }

    [Fact]
    public async Task Post_rejects_missing_empty_and_whitespace_completion_notes_without_changing_the_draft()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Oracle instrument")));
        foreach (var note in new string?[] { null, "", " \t " })
        {
            var fields = new List<(string Name, object Value)> { ("equipment_id", equipment.Id), ("result", "Passed") };
            if (note is not null)
                fields.Add(("completion_note", note));
            var draft = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection", Payload(fields.ToArray()));
            var path = $"/api/documents/example.inspection/{draft.Id}";
            var before = await admin.GetStringAsync(path);
            var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);

            using var rejected = await ExecutePostAsync(admin, path, state!.DocumentVersion, Guid.NewGuid().ToString("N"));

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before, await admin.GetStringAsync(path));
            var after = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
            Assert.Equal(state.DocumentVersion, after!.DocumentVersion);
            Assert.Equal(DocumentStatus.Draft, after.Document.Status);
            await AssertNoEffectsAsync(admin, path);
        }
    }

    [Fact]
    public async Task Completion_note_does_not_replace_existing_equipment_and_result_requirements()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Oracle instrument")));
        var payloads = new[]
        {
            Payload(("result", "Passed"), ("completion_note", "Reviewed")),
            Payload(("equipment_id", equipment.Id), ("completion_note", "Reviewed"))
        };
        foreach (var payload in payloads)
        {
            var draft = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection", payload);
            var path = $"/api/documents/example.inspection/{draft.Id}";
            var before = await admin.GetStringAsync(path);
            var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);

            using var rejected = await ExecutePostAsync(admin, path, state!.DocumentVersion, Guid.NewGuid().ToString("N"));

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before, await admin.GetStringAsync(path));
            await AssertNoEffectsAsync(admin, path);
        }
    }

    [Fact]
    public async Task Completion_note_length_limit_rejects_create_and_update_without_truncating_valid_text()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var valid = new string('x', 2000);
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection", Payload(("completion_note", valid)));
        var path = $"/api/documents/example.inspection/{draft.Id}";
        var saved = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal(valid, saved!.Payload.Fields!["completion_note"].GetString());
        var before = await admin.GetStringAsync(path);

        using var create = await admin.PostAsJsonAsync("/api/documents/example.inspection", Payload(("completion_note", valid + "x")));
        using var update = await admin.PutAsJsonAsync(path, Payload(("completion_note", valid + "x")));

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal(before, await admin.GetStringAsync(path));
    }

    [Fact]
    public async Task Valid_note_round_trips_and_posts_with_the_existing_empty_handler_without_effects()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CompleteDraftAsync(admin);

        using var response = await ExecutePostAsync(admin, path, version, "oracle-note-post");
        var posted = await ReadActionAsync(response);

        Assert.Equal(DocumentStatus.Posted, posted.Document.Status);
        Assert.Equal(version + 1, posted.DocumentVersion);
        var saved = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal("Calibrated and inspected", saved!.Payload.Fields!["completion_note"].GetString());
        await AssertNoEffectsAsync(admin, path);
        using var replay = await ExecutePostAsync(admin, path, version, "oracle-note-post");
        var repeated = await ReadActionAsync(replay);
        Assert.Equal(posted.ExecutionId, repeated.ExecutionId);
        Assert.Equal(posted.DocumentVersion, repeated.DocumentVersion);
        await AssertNoEffectsAsync(admin, path);
    }

    [Fact]
    public async Task Stale_action_version_is_rejected_after_posting_with_a_completion_note()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CompleteDraftAsync(admin);
        using var response = await ExecutePostAsync(admin, path, version, "oracle-current-note");
        var posted = await ReadActionAsync(response);
        Assert.True(posted.DocumentVersion > version);

        using var stale = await ExecuteActionAsync(admin, path, "unpost", version, "oracle-stale-note");

        await AssertErrorAsync(stale, HttpStatusCode.Conflict, "document.version_conflict");
        var current = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.Equal(posted.DocumentVersion, current!.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, current.Document.Status);
        Assert.Equal("Calibrated and inspected", current.Document.Payload.Fields!["completion_note"].GetString());
        await AssertNoEffectsAsync(admin, path);
    }

    [Fact]
    public async Task Missing_posting_handler_remains_refused_even_with_a_valid_note()
    {
        await using var host = new InspectionHost(registerWorkflowHandler: false);
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CompleteDraftAsync(admin);

        using var response = await ExecutePostAsync(admin, path, version, "oracle-missing-handler");

        await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "doc.posting.handler.not_configured");
        var saved = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal(DocumentStatus.Draft, saved!.Status);
        await AssertNoEffectsAsync(admin, path);
    }

    private static async Task<(string Path, long Version)> CompleteDraftAsync(HttpClient admin)
    {
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Oracle instrument")));
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            Payload(("equipment_id", equipment.Id), ("result", "Passed"), ("completion_note", "Calibrated and inspected")));
        var path = $"/api/documents/example.inspection/{draft.Id}";
        var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        return (path, state!.DocumentVersion);
    }
}
