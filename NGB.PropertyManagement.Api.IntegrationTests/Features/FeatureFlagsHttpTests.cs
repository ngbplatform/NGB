using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.Audit;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_configuration_starts_without_MinIO_and_content_endpoints_fail_closed(bool expirationEnabled)
    {
        var configuration = DisabledStorage();
        configuration["Attachments:UploadExpirationEnabled"] = expirationEnabled.ToString();
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
            () => client.PostAsync($"/api/attachments/{id}/audit-download", null),
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
            using var auditResponse = await client.GetAsync($"/api/audit/entities/2/{target.Id}");
            auditResponse.EnsureSuccessStatusCode();
            auditResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
            var audit = (await auditResponse.Content.ReadFromJsonAsync<AuditLogPageDto>())!;
            audit.Items.Count(x => x.ActionCode.StartsWith("notes.")).Should().Be(2);
            audit.Items.Single(x => x.ActionCode == "notes.updated").Changes
                .Should().Contain(x => x.FieldPath == "note.text" && x.NewValueJson == "\"Updated note\"");
        }

        configuration["FeatureManagement:Notes"] = "false";
        await using (var factory = new PmApiFactory(fixture, configuration))
        using (var client = factory.CreateClient())
        {
            using var denied = await client.DeleteAsync($"/api/notes/{noteId}?version=2");
            denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var audit = await client.GetFromJsonAsync<AuditLogPageDto>($"/api/audit/entities/2/{target.Id}");
            audit!.Items.Should().NotContain(x => x.ActionCode.StartsWith("notes."));
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
            var audit = await client.GetFromJsonAsync<AuditLogPageDto>($"/api/audit/entities/2/{target.Id}");
            audit!.Items.Single(x => x.ActionCode == "notes.mark_for_deletion").Changes
                .Should().Contain(x => x.FieldPath == "note.text" && x.NewValueJson == "\"Updated note\"");
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

    [Fact]
    public async Task Deleted_attachment_is_hidden_from_the_list_but_downloadable_from_parent_audit()
    {
        var storage = new Mock<IAttachmentObjectStorage>(MockBehavior.Strict);
        storage.Setup(x => x.CreateUploadTargetAsync(
                It.IsAny<string>(), "text/plain", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentUploadTarget("https://storage.test/upload", new Dictionary<string, string>()));
        storage.Setup(x => x.GetObjectInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentStoredObject(3, "text/plain", "retained-etag"));
        storage.Setup(x => x.SealUploadAsync(
                It.IsAny<string>(), It.IsAny<string>(), "retained-etag", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storage.Setup(x => x.CreateDownloadTargetAsync(
                It.IsAny<string>(), "retained.txt", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.test/retained-download");
        await using var root = new PmApiFactory(fixture, new Dictionary<string, string?>
        {
            ["FeatureManagement:Attachments"] = "true",
            ["FeatureManagement:Notes"] = "false",
            ["Attachments:UploadExpirationEnabled"] = "false"
        });
        await using var app = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAttachmentObjectStorage>();
            services.AddSingleton(storage.Object);
        }));
        using var identity = root.CreateClient();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = identity.DefaultRequestHeaders.Authorization;
        var target = await CreatePartyAsync(client);

        using var upload = await client.PostAsJsonAsync("/api/attachments/uploads",
            new CreateAttachmentUploadRequest(target, "retained.txt", "text/plain", 3));
        upload.EnsureSuccessStatusCode();
        var pending = (await upload.Content.ReadFromJsonAsync<AttachmentUploadDto>())!;
        using var complete = await client.PostAsync($"/api/attachments/{pending.AttachmentId}/complete", null);
        complete.EnsureSuccessStatusCode();
        using var deleted = await client.DeleteAsync($"/api/attachments/{pending.AttachmentId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<BusinessObjectPage<AttachmentDto>>(
            $"/api/attachments?kind=CatalogItem&typeCode=pm.party&objectId={target.Id}");
        list!.Items.Should().BeEmpty();
        using var directDownload = await client.PostAsync($"/api/attachments/{pending.AttachmentId}/download", null);
        directDownload.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await directDownload.Content.ReadAsStringAsync()).Should().Contain("attachments.deleted");
        var audit = await client.GetFromJsonAsync<AuditLogPageDto>($"/api/audit/entities/2/{target.Id}");
        var deletion = audit!.Items.Single(item => item.ActionCode == "attachments.mark_for_deletion");
        using var metadata = JsonDocument.Parse(deletion.MetadataJson!);
        metadata.RootElement.GetProperty("attachmentId").GetGuid().Should().Be(pending.AttachmentId);
        metadata.RootElement.GetProperty("downloadAvailable").GetBoolean().Should().BeTrue();

        using var download = await client.PostAsync($"/api/attachments/{pending.AttachmentId}/audit-download", null);
        download.EnsureSuccessStatusCode();
        download.Headers.CacheControl!.NoStore.Should().BeTrue();
        var result = (await download.Content.ReadFromJsonAsync<AttachmentDownloadDto>())!;
        result.Url.Should().Be("https://storage.test/retained-download");
        result.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        storage.Verify(x => x.CreateDownloadTargetAsync(
            $"attachments/{pending.AttachmentId:N}", "retained.txt", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Dictionary<string, string?> DisabledStorage() => new()
    {
        ["FeatureManagement:Attachments"] = "false",
        ["FeatureManagement:Notes"] = "false",
        ["Attachments:UploadExpirationEnabled"] = "false",
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
