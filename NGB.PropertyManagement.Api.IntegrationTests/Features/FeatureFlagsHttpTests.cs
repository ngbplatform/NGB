using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Features;
using NGB.Contracts.Notes;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Features;

[Collection(PmIntegrationCollection.Name)]
public sealed class FeatureFlagsHttpTests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Legacy_configuration_starts_without_MinIO_and_content_endpoints_fail_closed()
    {
        var configuration = DisabledStorage();
        configuration.Remove("FeatureManagement:Attachments");
        configuration.Remove("FeatureManagement:Notes");
        await using var factory = new PmApiFactory(fixture, configuration);
        using var client = factory.CreateClient();
        var states = await client.GetFromJsonAsync<FeatureStateDto[]>("/api/features");
        states.Should().HaveCount(2).And.OnlyContain(x => !x.Enabled);
        factory.Services.GetService<IAttachmentObjectStorage>().Should().BeNull();
        (await client.GetAsync("/api/security/me/access")).StatusCode.Should().Be(HttpStatusCode.OK);

        var id = Guid.NewGuid();
        var target = new BusinessObjectRef(BusinessObjectKind.CatalogItem, "pm.party", id);
        var query = $"kind=CatalogItem&typeCode=pm.party&objectId={id}";
        var calls = new Func<Task<HttpResponseMessage>>[]
        {
            () => client.GetAsync($"/api/attachments?{query}"),
            () => client.PostAsJsonAsync("/api/attachments/uploads", new CreateAttachmentUploadRequest(target, "file.txt", "text/plain", 3)),
            () => client.PostAsync($"/api/attachments/{id}/complete", null),
            () => client.PostAsync($"/api/attachments/{id}/download", null),
            () => client.DeleteAsync($"/api/attachments/{id}"),
            () => client.GetAsync($"/api/notes?{query}"),
            () => client.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "text")),
            () => client.PutAsJsonAsync($"/api/notes/{id}", new UpdateNoteRequest("text", 1)),
            () => client.DeleteAsync($"/api/notes/{id}?version=1"),
            () => client.GetAsync($"/api/business-objects/content-summary?{query}")
        };

        foreach (var call in calls)
        {
            using var response = await call();
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Contain("feature.disabled");
        }

        using var anonymous = factory.CreateAnonymousClient();
        (await anonymous.GetAsync("/api/features")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Notes_work_without_MinIO_and_survive_disabling_and_reenabling_the_feature()
    {
        var configuration = DisabledStorage();
        configuration["FeatureManagement:Notes"] = "true";
        Guid noteId;
        BusinessObjectRef target;

        await using (var factory = new PmApiFactory(fixture, configuration))
        using (var client = factory.CreateClient())
        {
            factory.Services.GetService<IAttachmentObjectStorage>().Should().BeNull();
            target = await CreatePartyAsync(client);
            using var created = await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "Retained note"));
            created.EnsureSuccessStatusCode();
            var note = (await created.Content.ReadFromJsonAsync<NoteDto>())!;
            noteId = note.Id;
            using var updated = await client.PutAsJsonAsync($"/api/notes/{noteId}", new UpdateNoteRequest("Updated note", note.Version));
            updated.EnsureSuccessStatusCode();
            var summary = await client.GetFromJsonAsync<BusinessObjectContentSummary>(
                $"/api/business-objects/content-summary?kind=CatalogItem&typeCode=pm.party&objectId={target.Id}");
            summary.Should().Be(new BusinessObjectContentSummary(null, 1));
        }

        configuration["FeatureManagement:Notes"] = "false";
        await using (var factory = new PmApiFactory(fixture, configuration))
        using (var client = factory.CreateClient())
        {
            using var denied = await client.DeleteAsync($"/api/notes/{noteId}?version=2");
            denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        configuration["FeatureManagement:Notes"] = "true";
        using (var factory = new PmApiFactory(fixture, configuration))
        using (var client = factory.CreateClient())
        {
            var page = await client.GetFromJsonAsync<BusinessObjectPage<NoteDto>>(
                $"/api/notes?kind=CatalogItem&typeCode=pm.party&objectId={target.Id}");
            page!.Items.Should().ContainSingle(x => x.Id == noteId && x.Text == "Updated note");
            using var deleted = await client.DeleteAsync($"/api/notes/{noteId}?version=2");
            deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_storage_is_isolated_and_notes_follow_their_own_flag(bool notesEnabled)
    {
        var configuration = DisabledStorage();
        configuration["FeatureManagement:Attachments"] = "true";
        configuration["FeatureManagement:Notes"] = notesEnabled.ToString();
        configuration["Attachments:MinIO:InternalEndpoint"] = "http://127.0.0.1:1";
        configuration["Attachments:MinIO:PublicEndpoint"] = "http://127.0.0.1:1";
        configuration["Attachments:MinIO:AllowInsecureHttp"] = "true";
        configuration["Attachments:MinIO:AccessKey"] = "test-access";
        configuration["Attachments:MinIO:SecretKey"] = "test-secret";
        configuration["Attachments:MinIO:RequestTimeoutSeconds"] = "1";
        await using var factory = new PmApiFactory(fixture, configuration);
        using var client = factory.CreateClient();
        var target = await CreatePartyAsync(client);
        using var upload = await client.PostAsJsonAsync("/api/attachments/uploads", new CreateAttachmentUploadRequest(target, "file.txt", "text/plain", 3));
        upload.EnsureSuccessStatusCode();
        var pending = (await upload.Content.ReadFromJsonAsync<AttachmentUploadDto>())!;
        using var complete = await client.PostAsync($"/api/attachments/{pending.AttachmentId}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var note = await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "Available without storage"));
        if (notesEnabled)
            note.EnsureSuccessStatusCode();
        else
            note.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.GetAsync("/api/security/me/access")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Dictionary<string, string?> DisabledStorage() => new()
    {
        ["FeatureManagement:Attachments"] = "false",
        ["FeatureManagement:Notes"] = "false",
        ["Attachments:MaintenanceEnabled"] = "false",
        ["Attachments:MinIO:InternalEndpoint"] = "",
        ["Attachments:MinIO:PublicEndpoint"] = "",
        ["Attachments:MinIO:AccessKey"] = "",
        ["Attachments:MinIO:SecretKey"] = ""
    };

    private static async Task<BusinessObjectRef> CreatePartyAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/catalogs/pm.party", new { fields = new { display = "Feature flags fixture" } });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new BusinessObjectRef(BusinessObjectKind.CatalogItem, "pm.party", created.GetProperty("id").GetGuid());
    }
}
