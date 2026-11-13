using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionLifecycleTests
{
    [Fact]
    public async Task Post_replay_repost_and_unpost_preserve_versions_and_empty_effects()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CreateCompleteInspectionAsync(admin);

        using var response = await ExecutePostAsync(admin, path, version, "post-inspection");
        var posted = await ReadActionAsync(response);
        Assert.Equal(DocumentStatus.Posted, posted.Document.Status);
        Assert.Equal(version + 1, posted.DocumentVersion);
        Assert.NotEqual(Guid.Empty, posted.ExecutionId);
        await AssertNoEffectsAsync(admin, path);

        using var retry = await ExecutePostAsync(admin, path, version, "post-inspection");
        var replayed = await ReadActionAsync(retry);
        await AssertEquivalentJsonAsync(response, retry);
        Assert.Equal(posted.ExecutionId, replayed.ExecutionId);
        Assert.Equal(posted.DocumentVersion, replayed.DocumentVersion);

        using var conflictingKey = await ExecutePostAsync(admin, path, posted.DocumentVersion, "post-inspection");
        await AssertErrorAsync(conflictingKey, HttpStatusCode.Conflict, "document_action.idempotency_conflict");
        using var stale = await ExecuteActionAsync(admin, path, "unpost", version, "stale-inspection");
        await AssertErrorAsync(stale, HttpStatusCode.Conflict, "document.version_conflict");
        var unchanged = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.Equal(posted.DocumentVersion, unchanged!.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, unchanged.Document.Status);
        await AssertNoEffectsAsync(admin, path);

        var unavailablePost = Assert.Single(unchanged.Actions, action => action.Code == "post");
        Assert.False(unavailablePost.IsAllowed);
        Assert.NotEmpty(unavailablePost.DisabledReasons);
        using var unavailable = await ExecutePostAsync(admin, path, posted.DocumentVersion, "already-posted");
        await AssertErrorAsync(unavailable, HttpStatusCode.Conflict, "document_action.unavailable");

        using var repostResponse = await ExecuteActionAsync(admin, path, "repost", posted.DocumentVersion, "repost-inspection");
        var reposted = await ReadActionAsync(repostResponse);
        Assert.Equal(DocumentStatus.Posted, reposted.Document.Status);
        Assert.Equal(posted.DocumentVersion + 1, reposted.DocumentVersion);
        Assert.NotEqual(posted.ExecutionId, reposted.ExecutionId);
        await AssertNoEffectsAsync(admin, path);

        using var unpostResponse = await ExecuteActionAsync(admin, path, "unpost", reposted.DocumentVersion, "unpost-inspection");
        var unposted = await ReadActionAsync(unpostResponse);
        Assert.Equal(DocumentStatus.Draft, unposted.Document.Status);
        Assert.Equal(reposted.DocumentVersion + 1, unposted.DocumentVersion);
        await AssertNoEffectsAsync(admin, path);

        using var delayedReplay = await ExecutePostAsync(admin, path, version, "post-inspection");
        var original = await ReadActionAsync(delayedReplay);
        Assert.Equal(posted.ExecutionId, original.ExecutionId);
        Assert.Equal(posted.DocumentVersion, original.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, original.Document.Status);
        var current = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.Equal(unposted.DocumentVersion, current!.DocumentVersion);
        Assert.Equal(DocumentStatus.Draft, current.Document.Status);
    }

    [Fact]
    public async Task Concurrent_posts_with_distinct_keys_commit_exactly_one_version()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CreateCompleteInspectionAsync(admin);

        var responses = await Task.WhenAll(
            ExecutePostAsync(admin, path, version, "concurrent-first"),
            ExecutePostAsync(admin, path, version, "concurrent-second"));
        using var first = responses[0];
        using var second = responses[1];
        var winner = Assert.Single(responses, response => response.IsSuccessStatusCode);
        var loser = Assert.Single(responses, response => !response.IsSuccessStatusCode);
        var posted = await ReadActionAsync(winner);
        await AssertErrorAsync(loser, HttpStatusCode.Conflict, "document.version_conflict");
        var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.Equal(version + 1, state!.DocumentVersion);
        Assert.Equal(posted.DocumentVersion, state.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, state.Document.Status);
        await AssertNoEffectsAsync(admin, path);
    }

    [Fact]
    public async Task Concurrent_repeats_of_one_post_return_one_completed_execution()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CreateCompleteInspectionAsync(admin);
        var responses = await Task.WhenAll(
            ExecutePostAsync(admin, path, version, "same-post"),
            ExecutePostAsync(admin, path, version, "same-post"));
        using var first = responses[0];
        using var second = responses[1];
        var original = await ReadActionAsync(first);
        var repeated = await ReadActionAsync(second);

        Assert.Equal(original.ExecutionId, repeated.ExecutionId);
        Assert.Equal(version + 1, original.DocumentVersion);
        Assert.Equal(original.DocumentVersion, repeated.DocumentVersion);
        var current = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.Equal(version + 1, current!.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, current.Document.Status);
        await AssertNoEffectsAsync(admin, path);
    }

    [Fact]
    public async Task Unauthorized_actor_cannot_replay_an_administrator_action_result()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var (path, version) = await CreateCompleteInspectionAsync(admin);

        using var posted = await ExecutePostAsync(admin, path, version, "protected-replay");
        var original = await ReadActionAsync(posted);
        using var forbiddenRead = await reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        using var replay = await ExecutePostAsync(reader, path, version, "protected-replay");
        var body = await replay.Content.ReadAsStringAsync();
        Assert.True(replay.StatusCode == HttpStatusCode.Forbidden,
            $"Expected HTTP 403; received {(int)replay.StatusCode}. " +
            $"Response contains the protected document ID: {body.Contains(original.Document.Id.ToString(), StringComparison.Ordinal)}.");

        using var authorizedReplay = await ExecutePostAsync(admin, path, version, "protected-replay");
        Assert.Equal(original.ExecutionId, (await ReadActionAsync(authorizedReplay)).ExecutionId);
    }

    [Fact]
    public async Task Deactivated_actor_cannot_replay_its_own_completed_action()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        const string username = "application-administrator";
        await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Inspection supervisors");
        using var client = await host.CreateClientAsync(username);
        var (path, version) = await CreateCompleteInspectionAsync(client);
        const string key = "own-completed-inspection";
        using var posted = await ExecutePostAsync(client, path, version, key);
        var original = await ReadActionAsync(posted);

        await host.DeactivateUserAsync(username);
        using var deniedRead = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        using var deniedNewAction = await ExecuteActionAsync(client, path, "unpost",
            original.DocumentVersion, "inactive-new-action");
        Assert.Equal(HttpStatusCode.Forbidden, deniedNewAction.StatusCode);

        using var replay = await ExecutePostAsync(client, path, version, key);
        Assert.True(replay.StatusCode == HttpStatusCode.Forbidden,
            $"Deactivated actor: document read and new action return 403; own completed replay returns {(int)replay.StatusCode}.");
    }
}
