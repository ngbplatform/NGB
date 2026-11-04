using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Persistence.Security;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class NotesHttpTests
{
    [Theory]
    [InlineData("administrator", BusinessObjectKind.CatalogItem)]
    [InlineData("application-administrator", BusinessObjectKind.Document)]
    public async Task Administrators_manage_plain_text_notes_and_parent_audit_without_manual_grants(
        string username, BusinessObjectKind kind)
    {
        await using var host = new InspectionHost(notesEnabled: true);
        await host.StartAsync();
        if (username == "application-administrator")
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Renamed supervisors");
        using var admin = await host.CreateClientAsync(username);
        var target = await CreateTargetAsync(admin, kind);
        var note = await PostAsync<NoteDto>(admin, "/api/notes", new CreateNoteRequest(target, "  <b>Check guard</b>  "));

        Assert.Equal("<b>Check guard</b>", note.Text);
        Assert.Equal(1, note.Version);
        Assert.NotEqual(Guid.Empty, note.CreatedByUserId);
        Assert.Equal(DateTimeKind.Utc, note.CreatedAtUtc.Kind);
        Assert.Null(note.UpdatedAtUtc);
        Assert.Equal(note.Id, Assert.Single((await ListAsync(admin, target)).Items).Id);

        using var updated = await admin.PutAsJsonAsync($"/api/notes/{note.Id}", new UpdateNoteRequest("Guard checked", note.Version));
        await EnsureSuccessAsync(updated);
        var changed = (await updated.Content.ReadFromJsonAsync<NoteDto>(Json))!;
        Assert.Equal(2, changed.Version);
        Assert.Equal("Guard checked", changed.Text);
        Assert.Equal(note.CreatedByUserId, changed.UpdatedByUserId);
        Assert.True(changed.UpdatedAtUtc >= note.CreatedAtUtc);

        using var staleUpdate = await admin.PutAsJsonAsync($"/api/notes/{note.Id}", new UpdateNoteRequest("Stale overwrite", note.Version));
        await AssertErrorAsync(staleUpdate, HttpStatusCode.Conflict, "notes.version_conflict");
        using var staleDelete = await admin.DeleteAsync($"/api/notes/{note.Id}?version={note.Version}");
        await AssertErrorAsync(staleDelete, HttpStatusCode.Conflict, "notes.version_conflict");
        using var deleted = await admin.DeleteAsync($"/api/notes/{note.Id}?version={changed.Version}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var repeatedDelete = await admin.DeleteAsync($"/api/notes/{note.Id}?version={changed.Version}");
        Assert.Equal(HttpStatusCode.NoContent, repeatedDelete.StatusCode);
        Assert.Empty((await ListAsync(admin, target)).Items);
        using var deletedUpdate = await admin.PutAsJsonAsync($"/api/notes/{note.Id}", new UpdateNoteRequest("Restore", changed.Version + 1));
        await AssertErrorAsync(deletedUpdate, HttpStatusCode.Conflict, "notes.deleted");

        var audit = await AuditAsync(admin, target);
        var events = audit.Items.Where(item => item.ActionCode.StartsWith("notes.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, events.Length);
        Assert.All(events, item =>
        {
            Assert.Equal(target.Id, item.EntityId);
            Assert.Equal(note.CreatedByUserId, item.Actor!.UserId);
            var metadata = JsonSerializer.Deserialize<JsonElement>(item.MetadataJson!);
            Assert.Equal(note.Id, metadata.GetProperty("noteId").GetGuid());
            Assert.Equal(target.Id, metadata.GetProperty("target").GetProperty("id").GetGuid());
            Assert.Equal(target.TypeCode, metadata.GetProperty("target").GetProperty("typeCode").GetString());
        });
        var edit = Assert.Single(events, item => item.ActionCode == "notes.updated");
        var text = Assert.Single(edit.Changes, change => change.FieldPath == "note.text");
        Assert.Equal(note.Text, JsonSerializer.Deserialize<string>(text.OldValueJson!));
        Assert.Equal(changed.Text, JsonSerializer.Deserialize<string>(text.NewValueJson!));
        var deletion = Assert.Single(events, item => item.ActionCode == "notes.mark_for_deletion");
        Assert.True(JsonSerializer.Deserialize<bool>(Assert.Single(deletion.Changes,
            change => change.FieldPath == "note.is_deleted").NewValueJson!));
    }

    [Fact]
    public async Task Content_permissions_and_parent_access_are_both_required_and_audit_filters_note_text()
    {
        await using var host = new InspectionHost(notesEnabled: true);
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var target = await CreateTargetAsync(admin, BusinessObjectKind.Document);
        var note = await PostAsync<NoteDto>(admin, "/api/notes", new CreateNoteRequest(target, "Private inspection note"));
        var view = new NgbPermissionKey(NgbResourceKinds.Document, target.TypeCode, NgbPermissionActions.View);
        var audit = new NgbPermissionKey(NgbResourceKinds.Document, target.TypeCode, NgbPermissionActions.ViewAudit);

        await GrantAsync(host, "reader", view, audit);
        using var reader = await host.CreateClientAsync("reader");
        using var deniedList = await reader.GetAsync(ListPath(target));
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);
        var filtered = await AuditAsync(reader, target, 1);
        Assert.Empty(filtered.Items);
        Assert.NotNull(filtered.NextCursor);
        var after = await AuditAsync(reader, target, 100, filtered.NextCursor);
        Assert.NotEmpty(after.Items);
        Assert.DoesNotContain(after.Items, item => item.ActionCode.StartsWith("notes.", StringComparison.Ordinal));
        Assert.DoesNotContain(note.Text, JsonSerializer.Serialize(after));

        await GrantAsync(host, "reader", view, audit, NgbSystemPermissions.NotesRead);
        Assert.Equal(note.Id, Assert.Single((await ListAsync(reader, target)).Items).Id);
        Assert.Contains((await AuditAsync(reader, target)).Items, item => item.ActionCode == "notes.created");
        using var deniedCreate = await reader.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "Reader mutation"));
        using var deniedUpdate = await reader.PutAsJsonAsync($"/api/notes/{note.Id}", new UpdateNoteRequest("Reader mutation", note.Version));
        using var deniedDelete = await reader.DeleteAsync($"/api/notes/{note.Id}?version={note.Version}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedDelete.StatusCode);

        await GrantAsync(host, "imitator", NgbSystemPermissions.NotesRead, NgbSystemPermissions.NotesCreate,
            NgbSystemPermissions.NotesUpdate, NgbSystemPermissions.NotesDelete);
        using var contentOnly = await host.CreateClientAsync("imitator");
        using var parentList = await contentOnly.GetAsync(ListPath(target));
        using var parentCreate = await contentOnly.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "Missing parent access"));
        using var parentUpdate = await contentOnly.PutAsJsonAsync($"/api/notes/{note.Id}", new UpdateNoteRequest("Missing parent access", note.Version));
        using var parentDelete = await contentOnly.DeleteAsync($"/api/notes/{note.Id}?version={note.Version}");
        Assert.Equal(HttpStatusCode.Forbidden, parentList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parentCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parentUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parentDelete.StatusCode);
        using var deniedAudit = await contentOnly.GetAsync(AuditPath(target));
        Assert.Equal(HttpStatusCode.Forbidden, deniedAudit.StatusCode);

        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        using var inactive = await host.CreateClientAsync("inactive-administrator");
        using var inactiveList = await inactive.GetAsync(ListPath(target));
        using var inactiveCreate = await inactive.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "Inactive mutation"));
        Assert.Equal(HttpStatusCode.Forbidden, inactiveList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inactiveCreate.StatusCode);
        Assert.Equal(note.Text, Assert.Single((await ListAsync(admin, target)).Items).Text);
        Assert.Single((await AuditAsync(admin, target)).Items, item => item.ActionCode.StartsWith("notes.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Note_paging_validation_and_concurrent_edits_preserve_one_winning_version()
    {
        await using var host = new InspectionHost(notesEnabled: true);
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var target = await CreateTargetAsync(admin, BusinessObjectKind.CatalogItem);
        var expected = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            var created = await PostAsync<NoteDto>(admin, "/api/notes", new CreateNoteRequest(target, "Note " + index));
            expected.Add(created.Id);
        }
        var listed = new List<Guid>();
        var cursors = new HashSet<Guid>();
        Guid? cursor = null;
        do
        {
            var page = await ListAsync(admin, target, 2, cursor);
            Assert.InRange(page.Items.Count, 1, 2);
            listed.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
            if (cursor.HasValue)
                Assert.True(cursors.Add(cursor.Value));
            Assert.InRange(listed.Count, 1, 5);
        } while (cursor.HasValue);
        Assert.Equal(expected.AsEnumerable().Reverse(), listed);

        foreach (var value in new[] { " ", "bad\0text", new string('x', 10001) })
        {
            using var invalid = await admin.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, value));
            await AssertErrorAsync(invalid, HttpStatusCode.BadRequest, value.Length > 10000 ? "notes.too_long" : "notes.invalid_text");
        }
        foreach (var query in new[] { "&limit=0", "&limit=101", "&cursor=" + Guid.Empty })
        {
            using var invalid = await admin.GetAsync(ListPath(target) + query);
            await AssertErrorAsync(invalid, HttpStatusCode.BadRequest, "business_object.invalid_page");
        }
        using var missing = await admin.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target with { Id = Guid.CreateVersion7() }, "Unknown parent"));
        await AssertErrorAsync(missing, HttpStatusCode.NotFound, "business_object.not_found");

        var id = expected[0];
        var responses = await Task.WhenAll(
            admin.PutAsJsonAsync($"/api/notes/{id}", new UpdateNoteRequest("First concurrent edit", 1)),
            admin.PutAsJsonAsync($"/api/notes/{id}", new UpdateNoteRequest("Second concurrent edit", 1)));
        using var first = responses[0];
        using var second = responses[1];
        var winner = Assert.Single(responses, response => response.IsSuccessStatusCode);
        await AssertErrorAsync(Assert.Single(responses, response => !response.IsSuccessStatusCode), HttpStatusCode.Conflict, "notes.version_conflict");
        var changed = (await winner.Content.ReadFromJsonAsync<NoteDto>(Json))!;
        Assert.Equal(2, changed.Version);
        Assert.Equal(changed.Text, Assert.Single((await ListAsync(admin, target)).Items, item => item.Id == id).Text);
        Assert.Single((await AuditAsync(admin, target)).Items, item => item.ActionCode == "notes.updated");
    }

    [Fact]
    public async Task Disabled_notes_remain_unavailable_to_administrators_and_anonymous_requests_are_rejected()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var target = await CreateTargetAsync(admin, BusinessObjectKind.CatalogItem);
        var id = Guid.CreateVersion7();
        using var list = await admin.GetAsync(ListPath(target));
        using var create = await admin.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "Feature disabled"));
        using var update = await admin.PutAsJsonAsync($"/api/notes/{id}", new UpdateNoteRequest("Feature disabled", 1));
        using var delete = await admin.DeleteAsync($"/api/notes/{id}?version=1");
        foreach (var response in new[] { list, create, update, delete })
            await AssertErrorAsync(response, HttpStatusCode.NotFound, "feature.disabled");
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync(ListPath(target));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    private static async Task<BusinessObjectRef> CreateTargetAsync(HttpClient client, BusinessObjectKind kind)
    {
        if (kind == BusinessObjectKind.CatalogItem)
        {
            var item = await PostAsync<CatalogItemDto>(client, "/api/catalogs/" + EquipmentDefinitions.EquipmentCode,
                Payload(("display", "Equipment with notes")));
            return new(kind, EquipmentDefinitions.EquipmentCode, item.Id);
        }
        var document = await PostAsync<DocumentDto>(client, "/api/documents/" + EquipmentDefinitions.InspectionCode, Payload());
        return new(kind, EquipmentDefinitions.InspectionCode, document.Id);
    }

    private static string ListPath(BusinessObjectRef target)
        => $"/api/notes?kind={target.Kind}&typeCode={target.TypeCode}&objectId={target.Id}";

    private static async Task<BusinessObjectPage<NoteDto>> ListAsync(HttpClient client, BusinessObjectRef target, int limit = 50, Guid? cursor = null)
    {
        using var response = await client.GetAsync(ListPath(target) + $"&limit={limit}&cursor={cursor}");
        await EnsureSuccessAsync(response);
        Assert.True(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<BusinessObjectPage<NoteDto>>(Json))!;
    }

    private static string AuditPath(BusinessObjectRef target)
        => $"/api/audit/entities/{(target.Kind == BusinessObjectKind.CatalogItem ? "Catalog" : "Document")}/{target.Id}";

    private static async Task<AuditLogPageDto> AuditAsync(HttpClient client, BusinessObjectRef target, int limit = 100, AuditCursorDto? cursor = null)
    {
        var path = AuditPath(target) + $"?limit={limit}";
        if (cursor is not null)
            path += $"&afterOccurredAtUtc={Uri.EscapeDataString(cursor.OccurredAtUtc.ToString("O"))}&afterAuditEventId={cursor.AuditEventId}";
        using var response = await client.GetAsync(path);
        await EnsureSuccessAsync(response);
        Assert.True(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<AuditLogPageDto>(Json))!;
    }

    private static async Task GrantAsync(InspectionHost host, string username, params NgbPermissionKey[] permissions)
    {
        var code = "example.notes-" + username;
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<IPlatformRoleRepository>();
        var role = await roles.GetByCodeAsync(code);
        if (role is null)
        {
            await host.LinkUserAsync(username, true, code, "Note access " + username);
            role = await roles.GetByCodeAsync(code);
        }
        using var admin = await host.CreateClientAsync("administrator");
        var request = new ReplaceRolePermissionsRequestDto(permissions.Select(permission =>
            new PermissionAssignmentDto(permission.ResourceKind, permission.ResourceCode, permission.ActionCode)).ToArray());
        using var response = await admin.PutAsJsonAsync($"/api/security/roles/{role!.RoleId}/permissions", request);
        await EnsureSuccessAsync(response);
    }
}
