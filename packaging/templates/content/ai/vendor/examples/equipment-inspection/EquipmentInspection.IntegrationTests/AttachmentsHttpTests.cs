using System.Net;
using System.Net.Http.Json;
using System.Text;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Persistence.Security;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class AttachmentsHttpTests
{
    [Theory]
    [InlineData("administrator", BusinessObjectKind.CatalogItem)]
    [InlineData("application-administrator", BusinessObjectKind.Document)]
    public async Task Real_upload_is_sealed_once_and_logical_deletion_preserves_authorized_audit_download(
        string username, BusinessObjectKind kind)
    {
        await using var storage = new TestAttachmentStorage();
        await storage.StartAsync();
        await using var host = new InspectionHost(configuration: storage.Configuration);
        await host.StartAsync();
        if (username == "application-administrator")
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Renamed supervisors");
        using var admin = await host.CreateClientAsync(username);
        var target = await TargetAsync(admin, kind);
        var bytes = Encoding.UTF8.GetBytes("Inspection report: passed\n");
        var upload = await RequestUploadAsync(admin, target, bytes.Length, "../inspection.txt");
        Assert.Empty((await ListAsync(admin, target)).Items);
        using var missing = await admin.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        await AssertErrorAsync(missing, HttpStatusCode.Conflict, "attachments.upload_missing");
        using var pending = await admin.PostAsync($"/api/attachments/{upload.AttachmentId}/download", null);
        await AssertErrorAsync(pending, HttpStatusCode.Conflict, "attachments.not_ready");
        await UploadAsync(upload, bytes);

        var completions = await Task.WhenAll(
            admin.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null),
            admin.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null));
        using var first = completions[0];
        using var second = completions[1];
        foreach (var response in completions)
        {
            await EnsureSuccessAsync(response);
            var attachment = (await response.Content.ReadFromJsonAsync<AttachmentDto>(Json))!;
            Assert.Equal(upload.AttachmentId, attachment.Id);
            Assert.Equal("inspection.txt", attachment.FileName);
            Assert.Equal(bytes.Length, attachment.SizeBytes);
            Assert.Equal("text/plain", attachment.ContentType);
            Assert.NotEqual(Guid.Empty, attachment.CreatedByUserId);
        }
        Assert.Equal(upload.AttachmentId, Assert.Single((await ListAsync(admin, target)).Items).Id);
        await AssertDownloadAsync(admin, upload.AttachmentId, bytes);

        // A still-valid browser PUT can only change staging, never the sealed file.
        await UploadAsync(upload, Encoding.UTF8.GetBytes("Changed staging content"));
        await AssertDownloadAsync(admin, upload.AttachmentId, bytes);
        using (var raw = new HttpClient())
        {
            using var anonymous = await raw.GetAsync(new Uri(upload.Url).GetLeftPart(UriPartial.Path));
            Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
        }

        using var deleted = await admin.DeleteAsync($"/api/attachments/{upload.AttachmentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var repeated = await admin.DeleteAsync($"/api/attachments/{upload.AttachmentId}");
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Empty((await ListAsync(admin, target)).Items);
        using var unavailable = await admin.PostAsync($"/api/attachments/{upload.AttachmentId}/download", null);
        await AssertErrorAsync(unavailable, HttpStatusCode.Conflict, "attachments.deleted");
        await AssertDownloadAsync(admin, upload.AttachmentId, bytes, audit: true);

        var history = await AuditAsync(admin, target);
        var events = history.Items.Where(item => item.ActionCode.StartsWith("attachments.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, events.Length);
        Assert.Single(events, item => item.ActionCode == "attachments.upload_completed");
        Assert.Single(events, item => item.ActionCode == "attachments.mark_for_deletion");
        Assert.All(events, item =>
        {
            Assert.Equal(target.Id, item.EntityId);
            Assert.NotNull(item.Actor!.UserId);
            Assert.DoesNotContain("X-Amz-", item.MetadataJson!);
            Assert.DoesNotContain("uploads/", item.MetadataJson!);
        });
    }

    [Fact]
    public async Task Metadata_quota_and_expiration_are_enforced_without_publishing_unverified_objects()
    {
        await using var storage = new TestAttachmentStorage();
        await storage.StartAsync();
        var configuration = storage.Configuration.ToDictionary();
        configuration["Attachments:MaxActivePerObject"] = "2";
        configuration["Attachments:MaxSizeBytes"] = "1024";
        var clock = new AttachmentClock();
        await using var host = new InspectionHost(configuration: configuration,
            configureServices: services => services.AddSingleton<TimeProvider>(clock));
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var target = await TargetAsync(admin, BusinessObjectKind.CatalogItem);
        var first = await RequestUploadAsync(admin, target, 3);
        var second = await RequestUploadAsync(admin, target, 3);
        using var quota = await admin.PostAsJsonAsync("/api/attachments/uploads",
            new CreateAttachmentUploadRequest(target, "third.txt", "text/plain", 3));
        await AssertErrorAsync(quota, HttpStatusCode.BadRequest, "attachments.count_limit_exceeded");
        await UploadAsync(first, [1, 2]);
        using var size = await admin.PostAsync($"/api/attachments/{first.AttachmentId}/complete", null);
        await AssertErrorAsync(size, HttpStatusCode.Conflict, "attachments.size_mismatch");
        await UploadAsync(first, [1, 2, 3], "application/octet-stream");
        using var type = await admin.PostAsync($"/api/attachments/{first.AttachmentId}/complete", null);
        await AssertErrorAsync(type, HttpStatusCode.Conflict, "attachments.content_type_mismatch");
        Assert.Empty((await ListAsync(admin, target)).Items);
        await UploadAsync(first, [1, 2, 3]);
        using var complete = await admin.PostAsync($"/api/attachments/{first.AttachmentId}/complete", null);
        await EnsureSuccessAsync(complete);

        clock.Advance(TimeSpan.FromDays(2));
        using var expired = await admin.PostAsync($"/api/attachments/{second.AttachmentId}/complete", null);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var expiration = scope.ServiceProvider.GetRequiredService<IAttachmentUploadExpirationService>();
            Assert.InRange(await expiration.ExpirePendingAsync(CancellationToken.None), 0, 1);
            Assert.Equal(0, await expiration.ExpirePendingAsync(CancellationToken.None));
        }
        using var deleted = await admin.PostAsync($"/api/attachments/{second.AttachmentId}/complete", null);
        await AssertErrorAsync(deleted, HttpStatusCode.Conflict, "attachments.deleted");
        var replacement = await RequestUploadAsync(admin, target, 3);
        Assert.NotEqual(second.AttachmentId, replacement.AttachmentId);
        Assert.Single((await AuditAsync(admin, target)).Items, item => item.ActionCode == "attachments.upload_expired");

        foreach (var request in new[]
        {
            new CreateAttachmentUploadRequest(target, "..", "text/plain", 3),
            new CreateAttachmentUploadRequest(target, "invalid.txt", "text/plain\r\nX-Test: bad", 3),
            new CreateAttachmentUploadRequest(target, "large.txt", "text/plain", 1025)
        })
        {
            using var invalid = await admin.PostAsJsonAsync("/api/attachments/uploads", request);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task Storage_access_failure_rolls_back_completion_and_paging_lists_only_ready_files()
    {
        await using var storage = new TestAttachmentStorage();
        await storage.StartAsync();
        await using var host = new InspectionHost(configuration: storage.Configuration);
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var target = await TargetAsync(admin, BusinessObjectKind.Document);
        var upload = await RequestUploadAsync(admin, target, 3);
        await UploadAsync(upload, [1, 2, 3]);
        await storage.SetApplicationAccessAsync(false);
        using var failed = await admin.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Empty((await ListAsync(admin, target)).Items);
        Assert.DoesNotContain((await AuditAsync(admin, target)).Items, item => item.ActionCode == "attachments.upload_completed");
        await storage.SetApplicationAccessAsync(true);
        using var retry = await admin.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        await EnsureSuccessAsync(retry);
        var expected = new List<Guid> { upload.AttachmentId };
        for (var index = 0; index < 2; index++)
        {
            var next = await RequestUploadAsync(admin, target, 3);
            await UploadAsync(next, [4, 5, 6]);
            using var ready = await admin.PostAsync($"/api/attachments/{next.AttachmentId}/complete", null);
            await EnsureSuccessAsync(ready);
            expected.Add(next.AttachmentId);
        }
        await RequestUploadAsync(admin, target, 3);
        var first = await ListAsync(admin, target, 2);
        Assert.Equal(expected.AsEnumerable().Reverse().Take(2), first.Items.Select(item => item.Id));
        Assert.NotNull(first.NextCursor);
        var last = await ListAsync(admin, target, 2, first.NextCursor);
        Assert.Equal(upload.AttachmentId, Assert.Single(last.Items).Id);
        Assert.Null(last.NextCursor);
        Assert.Equal(3, (await AuditAsync(admin, target)).Items.Count(item => item.ActionCode == "attachments.upload_completed"));
    }

    [Fact]
    public async Task Attachment_permissions_never_replace_parent_access_and_disabled_features_stay_disabled()
    {
        await using var storage = new TestAttachmentStorage();
        await storage.StartAsync();
        await using var host = new InspectionHost(configuration: storage.Configuration);
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var target = await TargetAsync(admin, BusinessObjectKind.Document);
        var upload = await RequestUploadAsync(admin, target, 3);
        await UploadAsync(upload, [1, 2, 3]);
        await host.LinkUserAsync("reader", true, "example.attachment-reader", "Attachment reader");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var role = await scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>().GetByCodeAsync("example.attachment-reader");
            using var granted = await admin.PutAsJsonAsync($"/api/security/roles/{role!.RoleId}/permissions",
                new ReplaceRolePermissionsRequestDto(
                [
                    new PermissionAssignmentDto("system", "attachments", "read"),
                    new PermissionAssignmentDto("system", "attachments", "create"),
                    new PermissionAssignmentDto("system", "attachments", "delete")
                ]));
            await EnsureSuccessAsync(granted);
        }
        using var reader = await host.CreateClientAsync("reader");
        using var list = await reader.GetAsync(ListPath(target));
        using var create = await reader.PostAsJsonAsync("/api/attachments/uploads", new CreateAttachmentUploadRequest(target, "other.txt", "text/plain", 3));
        using var complete = await reader.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        using var download = await reader.PostAsync($"/api/attachments/{upload.AttachmentId}/download", null);
        using var delete = await reader.DeleteAsync($"/api/attachments/{upload.AttachmentId}");
        foreach (var denied in new[] { list, create, complete, download, delete })
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var ready = await admin.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        await EnsureSuccessAsync(ready);
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        using var inactive = await host.CreateClientAsync("inactive-administrator");
        using var inactiveDownload = await inactive.PostAsync($"/api/attachments/{upload.AttachmentId}/download", null);
        Assert.Equal(HttpStatusCode.Forbidden, inactiveDownload.StatusCode);
        using var missing = await admin.PostAsJsonAsync("/api/attachments/uploads",
            new CreateAttachmentUploadRequest(target with { Id = Guid.CreateVersion7() }, "unknown.txt", "text/plain", 3));
        await AssertErrorAsync(missing, HttpStatusCode.NotFound, "business_object.not_found");
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync(ListPath(target));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await using var disabledHost = new InspectionHost();
        await disabledHost.StartAsync();
        using var disabledAdmin = await disabledHost.CreateClientAsync("administrator");
        using var disabled = await disabledAdmin.PostAsJsonAsync("/api/attachments/uploads",
            new CreateAttachmentUploadRequest(target, "disabled.txt", "text/plain", 3));
        await AssertErrorAsync(disabled, HttpStatusCode.NotFound, "feature.disabled");
    }

    private static async Task<BusinessObjectRef> TargetAsync(HttpClient client, BusinessObjectKind kind)
    {
        if (kind == BusinessObjectKind.CatalogItem)
        {
            var item = await PostAsync<CatalogItemDto>(client, "/api/catalogs/" + EquipmentDefinitions.EquipmentCode,
                Payload(("display", "Equipment attachment parent")));
            return new(kind, EquipmentDefinitions.EquipmentCode, item.Id);
        }
        var document = await PostAsync<DocumentDto>(client, "/api/documents/" + EquipmentDefinitions.InspectionCode, Payload());
        return new(kind, EquipmentDefinitions.InspectionCode, document.Id);
    }

    private static Task<AttachmentUploadDto> RequestUploadAsync(HttpClient client, BusinessObjectRef target, int size, string name = "inspection.txt")
        => PostAsync<AttachmentUploadDto>(client, "/api/attachments/uploads", new CreateAttachmentUploadRequest(target, name, "text/plain", size));

    private static async Task UploadAsync(AttachmentUploadDto upload, byte[] bytes, string? contentType = null)
    {
        using var client = new HttpClient();
        using var content = new ByteArrayContent(bytes);
        foreach (var (name, value) in upload.Headers)
            Assert.True(content.Headers.TryAddWithoutValidation(name, contentType is not null && name == "Content-Type" ? contentType : value));
        using var response = await client.PutAsync(upload.Url, content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertDownloadAsync(HttpClient client, Guid id, byte[] expected, bool audit = false)
    {
        using var response = await client.PostAsync($"/api/attachments/{id}/{(audit ? "audit-download" : "download")}", null);
        await EnsureSuccessAsync(response);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var target = (await response.Content.ReadFromJsonAsync<AttachmentDownloadDto>(Json))!;
        Assert.True(target.ExpiresAtUtc > DateTime.UtcNow);
        using var storage = new HttpClient();
        using var downloaded = await storage.GetAsync(target.Url);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(expected, await downloaded.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/octet-stream", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.True(downloaded.Headers.CacheControl!.NoStore);
    }

    private static string ListPath(BusinessObjectRef target)
        => $"/api/attachments?kind={target.Kind}&typeCode={target.TypeCode}&objectId={target.Id}";

    private static async Task<BusinessObjectPage<AttachmentDto>> ListAsync(HttpClient client, BusinessObjectRef target, int limit = 50, Guid? cursor = null)
    {
        using var response = await client.GetAsync(ListPath(target) + $"&limit={limit}&cursor={cursor}");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<BusinessObjectPage<AttachmentDto>>(Json))!;
    }

    private static async Task<AuditLogPageDto> AuditAsync(HttpClient client, BusinessObjectRef target)
    {
        var kind = target.Kind == BusinessObjectKind.CatalogItem ? "Catalog" : "Document";
        return (await client.GetFromJsonAsync<AuditLogPageDto>($"/api/audit/entities/{kind}/{target.Id}?limit=100", Json))!;
    }

    private sealed class AttachmentClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.Ticks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
}
