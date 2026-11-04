using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Contracts.Attachments;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Contracts.Reporting;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using NGB.Contracts.WorkCenter;
using NGB.Core.WorkCenter;
using NGB.Mcp;
using NGB.Mcp.Tools;
using NGB.Persistence.AuditLog;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class McpBuiltInReadTests
{
    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Built_in_metadata_records_and_search_preserve_authorized_source_identity(string username)
    {
        await using var host = CreateHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Read integration supervisors");
        }
        using var admin = await host.CreateClientAsync(username, authorizationCode: true);
        var first = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "MCP instrument Alpha")));
        await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "MCP instrument Beta")));
        var path = $"/api/catalogs/example.equipment/{first.Id}";
        var before = await admin.GetStringAsync(path);
        var capabilities = await ReadAsync<CapabilitiesResult>(admin, McpToolNames.GetCapabilities, new { });
        Assert.True(capabilities.Data.ReadOnly);
        Assert.Contains("catalog", capabilities.Data.Categories);
        Assert.Contains(McpToolNames.GetRecord, capabilities.Data.Tools);
        Assert.Contains(McpToolNames.GetReportDefinition, capabilities.Data.Tools);

        var metadata = await ReadAsync<MetadataPage>(admin, McpToolNames.ListMetadata, new { kind = "catalog", limit = 1 });
        Assert.Single(metadata.Data.Items);
        Assert.Equal(1, metadata.Data.Limit);
        var definition = await ReadAsync<MetadataResult>(admin, McpToolNames.GetMetadata,
            new { kind = "catalog", typeCode = "example.equipment" });
        Assert.Equal("example.equipment", definition.Data.Catalog!.CatalogType);
        Assert.Null(definition.Data.Catalog.Capabilities);
        var records = await ReadAsync<RecordsResult>(admin, McpToolNames.ListRecords,
            new { kind = "catalog", typeCode = "example.equipment", search = "MCP instrument", limit = 1, includeTotal = true });
        Assert.Single(records.Data.Catalogs!.Items);
        Assert.Equal(2, records.Data.Catalogs.Total);

        var record = await ReadAsync<RecordResult>(admin, McpToolNames.GetRecord,
            new { kind = "catalog", typeCode = "example.equipment", id = first.Id });
        Assert.Equal(first.Id, record.Data.Catalog!.Id);
        Assert.Equal(first.Display, record.Data.Catalog.Display);
        Assert.Equal($"https://inspection.example.test/catalogs/example.equipment/{first.Id}", record.Data.Source!.OpenInNgb);
        Assert.Equal(first.Id, Assert.Single(record.Sources).Id);
        var search = await ReadAsync<SearchResult>(admin, McpToolNames.Search,
            new { query = "MCP instrument Alpha", kind = "catalog", typeCode = "example.equipment", limit = 1 });
        var hit = Assert.Single(search.Data.Items);
        Assert.Equal(first.Id, hit.RecordId);
        Assert.Equal($"catalog:example.equipment:{first.Id}", hit.Id);
        Assert.Equal(record.Data.Source.OpenInNgb, hit.Url);
        var fetched = await FetchAsync(admin, hit.Id);
        Assert.Equal(hit.Id, fetched.Id);
        Assert.Equal(first.Id, JsonSerializer.Deserialize<RecordResult>(fetched.Text, Json)!.Catalog!.Id);
        Assert.Equal(before, await admin.GetStringAsync(path));

        await host.LinkUserAsync("reader", true, "example.mcp-observer", "Read observer");
        using var reader = await host.CreateClientAsync("reader", authorizationCode: true);
        var hidden = await ReadAsync<MetadataPage>(reader, McpToolNames.ListMetadata, new { kind = "catalog" });
        Assert.DoesNotContain(hidden.Data.Items, item => item.TypeCode == "example.equipment");
        Assert.Equal("unavailable", await ErrorAsync(reader, McpToolNames.GetRecord,
            new { kind = "catalog", typeCode = "example.equipment", id = first.Id }));
        Assert.Equal("unavailable", await ErrorAsync(reader, McpToolNames.CompatibilityFetch, new { id = hit.Id }));
        var roles = (await admin.GetFromJsonAsync<RoleListItemDto[]>("/api/security/roles", Json))!;
        var role = Assert.Single(roles, item => item.Code == "example.mcp-observer");
        using var granted = await admin.PutAsJsonAsync($"/api/security/roles/{role.RoleId}/permissions",
            new ReplaceRolePermissionsRequestDto(
            [
                new("catalog", "example.equipment", "view"),
                new("catalog", "example.equipment", "lookup")
            ]));
        await EnsureSuccessAsync(granted);
        var compatible = await CallAsync(reader, McpToolNames.CompatibilitySearch, new { query = "MCP instrument Alpha" });
        Assert.False(compatible.TryGetProperty("isError", out var searchError) && searchError.GetBoolean(), compatible.GetRawText());
        var compatibleResult = compatible.GetProperty("structuredContent").Deserialize<CompatibilitySearchResult>(Json)!;
        Assert.Equal(hit.Id, Assert.Single(compatibleResult.Results).Id);
        Assert.Equal("invalid_input", await ErrorAsync(admin, McpToolNames.CompatibilityFetch,
            new { id = "https://untrusted.example.test/private" }));
        Assert.Equal("invalid_input", await ErrorAsync(admin, McpToolNames.ListRecords,
            new { kind = "catalog", typeCode = "example.equipment", limit = 101 }));
    }

    [Fact]
    public async Task General_journal_detail_exposes_the_existing_generic_metadata_limitation()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator", authorizationCode: true);
        var journal = await PostAsync<GeneralJournalEntryDetailsDto>(admin, "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(DateTime.UtcNow));
        var path = $"/api/accounting/general-journal-entries/{journal.Document.Id}";
        var before = await admin.GetStringAsync(path);
        Assert.Equal(journal.Document.Id, JsonSerializer.Deserialize<GeneralJournalEntryDetailsDto>(before, Json)!.Document.Id);
        var metadata = await ReadAsync<MetadataPage>(admin, McpToolNames.ListMetadata, new { kind = "generalJournalEntry" });
        Assert.Equal("general_journal_entry", Assert.Single(metadata.Data.Items).TypeCode);
        var records = await ReadAsync<RecordsResult>(admin, McpToolNames.ListRecords,
            new { kind = "generalJournalEntry", typeCode = "general_journal_entry", limit = 1 });
        Assert.Equal(journal.Document.Id, Assert.Single(records.Data.GeneralJournalEntries!.Items).Id);
        Assert.Equal("internal_error", await ErrorAsync(admin, McpToolNames.GetMetadata,
            new { kind = "generalJournalEntry", typeCode = "general_journal_entry" }));
        Assert.Equal("internal_error", await ErrorAsync(admin, McpToolNames.GetRecord,
            new { kind = "generalJournalEntry", typeCode = "general_journal_entry", id = journal.Document.Id }));
        Assert.Equal("internal_error", await ErrorAsync(admin, McpToolNames.CompatibilityFetch,
            new { id = $"generalJournalEntry:general_journal_entry:{journal.Document.Id}" }));
        Assert.Equal("invalid_input", await ErrorAsync(admin, McpToolNames.GetRecord,
            new { kind = "document", typeCode = "general_journal_entry", id = journal.Document.Id }));
        Assert.Equal(before, await admin.GetStringAsync(path));

        await using var scope = host.Services.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() =>
            scope.ServiceProvider.GetRequiredService<IDocumentService>()
                .GetTypeMetadataAsync("general_journal_entry", CancellationToken.None));
        Assert.Contains("display column", failure.Message);
    }

    [Fact]
    public async Task Built_in_document_graph_effects_and_reports_reuse_the_existing_read_contracts()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator", authorizationCode: true);
        var (path, version) = await CreateCompleteInspectionAsync(admin);
        using var response = await ExecutePostAsync(admin, path, version, "mcp-read-inspection");
        var posted = await ReadActionAsync(response);
        var before = await admin.GetStringAsync(path);
        var metadata = await ReadAsync<MetadataResult>(admin, McpToolNames.GetMetadata,
            new { kind = "document", typeCode = "example.inspection" });
        Assert.Empty(metadata.Data.Document!.Actions!);
        Assert.Null(metadata.Data.Document.Capabilities);
        var record = await ReadAsync<RecordResult>(admin, McpToolNames.GetRecord,
            new { kind = "document", typeCode = "example.inspection", id = posted.Document.Id });
        Assert.Equal(posted.Document.Id, record.Data.Document!.Id);
        Assert.Contains(record.Data.LookupFields!, item => item.Key == "equipment_id");
        var graph = await ReadAsync<FlowResult>(admin, McpToolNames.GetDocumentFlow,
            new { typeCode = "example.inspection", id = posted.Document.Id, depth = 1, maxNodes = 10 });
        Assert.Contains(graph.Data.Graph.Nodes, node => node.EntityId == posted.Document.Id);
        var effects = await ReadAsync<EffectsResult>(admin, McpToolNames.GetDocumentEffects,
            new { typeCode = "example.inspection", id = posted.Document.Id, limit = 1 });
        Assert.Equal("complete", effects.Data.Completeness);
        Assert.Equal("result_local", effects.Data.IdentifierScope);
        Assert.Empty(effects.Data.Effects.AccountingEntries);
        Assert.Empty(effects.Data.Effects.OperationalRegisterMovements);
        Assert.Empty(effects.Data.Effects.ReferenceRegisterWrites);

        var reports = await ReadAsync<ReportsResult>(admin, McpToolNames.ListReports, new { limit = 100 });
        Assert.Contains(reports.Data.Items, item => item.ReportCode == "example.inspection-summary");
        var report = await ReadAsync<ReportDefinitionDto>(admin, McpToolNames.GetReportDefinition,
            new { reportCode = "example.inspection-summary" });
        Assert.Equal("example.inspection-summary", report.Data.ReportCode);
        Assert.Equal(report.Data.ReportCode, Assert.Single(report.Sources).TypeCode);
        var request = new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
        {
            ["from_utc"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
            ["to_utc"] = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd")
        }, Limit: 10);
        var actual = await ReadAsync<ReportExecutionResponseDto>(admin, McpToolNames.ExecuteReport,
            new { reportCode = report.Data.ReportCode, request });
        var expected = await PostAsync<ReportExecutionResponseDto>(admin, "/api/reports/" + report.Data.ReportCode + "/execute", request);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected.Sheet, Json),
            JsonSerializer.SerializeToElement(actual.Data.Sheet, Json)));
        var fetched = await FetchAsync(admin, "report:" + report.Data.ReportCode);
        Assert.Equal(report.Data.ReportCode, JsonSerializer.Deserialize<ReportDefinitionDto>(fetched.Text, Json)!.ReportCode);
        Assert.Equal("invalid_input", await ErrorAsync(admin, McpToolNames.ExecuteReport,
            new { reportCode = report.Data.ReportCode, request = new { limit = 201 } }));
        Assert.Equal(before, await admin.GetStringAsync(path));
    }

    [Fact]
    public async Task Built_in_notes_audit_and_work_items_remain_reads_without_marking_notifications()
    {
        await using var host = CreateHost(notesEnabled: true);
        await host.StartAsync();
        await host.LinkUserAsync("administrator", true, InspectionHost.AdministratorRoleCode, "MCP content supervisors");
        using var admin = await host.CreateClientAsync("administrator", authorizationCode: true);
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "MCP content instrument")));
        var target = new BusinessObjectRef(BusinessObjectKind.CatalogItem, "example.equipment", equipment.Id);
        var first = await PostAsync<NoteDto>(admin, "/api/notes", new CreateNoteRequest(target, "Keep <b>this note</b> as data"));
        var second = await PostAsync<NoteDto>(admin, "/api/notes", new CreateNoteRequest(target, "Inspect the guard"));
        var summary = await ReadAsync<BusinessObjectContentSummary>(admin, McpToolNames.GetContentSummary,
            new { target = new { kind = "catalogItem", typeCode = target.TypeCode, id = target.Id } });
        Assert.Equal(2, summary.Data.Notes);
        Assert.Null(summary.Data.Attachments);
        var notes = await ReadAsync<BusinessObjectPage<NoteDto>>(admin, McpToolNames.ListNotes,
            new { target = new { kind = "catalogItem", typeCode = target.TypeCode, id = target.Id }, limit = 1 });
        Assert.Single(notes.Data.Items);
        Assert.NotNull(notes.Data.NextCursor);
        var next = await ReadAsync<BusinessObjectPage<NoteDto>>(admin, McpToolNames.ListNotes,
            new { target = new { kind = "catalogItem", typeCode = target.TypeCode, id = target.Id }, limit = 1, cursor = notes.Data.NextCursor });
        Assert.Null(next.Data.NextCursor);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), notes.Data.Items.Concat(next.Data.Items).Select(item => item.Id).Order());
        Assert.Equal(first.Text, Assert.Single(notes.Data.Items.Concat(next.Data.Items), item => item.Id == first.Id).Text);
        var auditBefore = await admin.GetStringAsync($"/api/audit/entities/Catalog/{target.Id}");
        var audit = await ReadAsync<AuditLogPageDto>(admin, McpToolNames.GetAuditHistory,
            new { kind = "catalog", id = target.Id, limit = 100 });
        Assert.Equal(2, audit.Data.Items.Count(item => item.ActionCode == "notes.created"));

        await using var scope = host.Services.CreateAsyncScope();
        var users = await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
            .GetByEmailsAsync(["administrator@example.test"]);
        var user = Assert.Single(users);
        var source = new WorkCenterSourceReference("catalog", target.TypeCode, target.Id, equipment.Display!, null);
        var created = await scope.ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(
            new CreateNotificationRequest(InspectionWorkCenterDefinitions.NotificationCode, source,
                "Read-only notification", "Keep unread", null, [user.UserId], null, "mcp-notification",
                Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);
        Assert.NotNull(created.ItemId);
        var workBefore = (await admin.GetFromJsonAsync<WorkCenterSummaryDto>("/api/work-center/summary", Json))!;
        Assert.Equal(1, workBefore.UnreadNotificationCount);
        var work = await ReadAsync<WorkItemsResult>(admin, McpToolNames.GetWorkItems,
            new { query = new { tab = "notifications", limit = 1 } });
        var notification = Assert.Single(work.Data.Page!.Items);
        Assert.Equal(created.ItemId, notification.Id);
        Assert.False(notification.IsRead);
        Assert.Null(notification.Target);
        Assert.Null(notification.PrimaryActionCode);
        var count = await ReadAsync<WorkItemsResult>(admin, McpToolNames.GetWorkItems, new { summaryOnly = true });
        Assert.Equal(workBefore, count.Data.Summary);
        Assert.Equal(workBefore, await admin.GetFromJsonAsync<WorkCenterSummaryDto>("/api/work-center/summary", Json));
        Assert.Equal(auditBefore, await admin.GetStringAsync($"/api/audit/entities/Catalog/{target.Id}"));
    }

    [Fact]
    public async Task Built_in_attachments_expose_ready_metadata_without_storage_keys_or_download_urls()
    {
        await using var storage = new TestAttachmentStorage();
        await storage.StartAsync();
        await using var host = CreateHost(configuration: storage.Configuration);
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator", authorizationCode: true);
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "MCP attachment instrument")));
        var target = new BusinessObjectRef(BusinessObjectKind.CatalogItem, "example.equipment", equipment.Id);
        var arguments = new { target = new { kind = "catalogItem", typeCode = target.TypeCode, id = target.Id }, limit = 1 };
        var bytes = "Private inspection attachment bytes"u8.ToArray();
        var upload = await PostAsync<AttachmentUploadDto>(admin, "/api/attachments/uploads",
            new CreateAttachmentUploadRequest(target, "inspection.txt", "text/plain", bytes.Length));
        var pending = await ReadAsync<BusinessObjectPage<AttachmentDto>>(admin, McpToolNames.ListAttachments, arguments);
        Assert.Empty(pending.Data.Items);
        using (var raw = new HttpClient())
        using (var content = new ByteArrayContent(bytes))
        {
            foreach (var (name, value) in upload.Headers)
            {
                Assert.True(content.Headers.TryAddWithoutValidation(name, value));
            }
            using var uploaded = await raw.PutAsync(upload.Url, content);
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        }
        using var complete = await admin.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        await EnsureSuccessAsync(complete);
        var auditPath = $"/api/audit/entities/Catalog/{target.Id}";
        var auditBefore = await admin.GetStringAsync(auditPath);
        var ready = await ReadAsync<BusinessObjectPage<AttachmentDto>>(admin, McpToolNames.ListAttachments, arguments);
        var attachment = Assert.Single(ready.Data.Items);
        Assert.Equal(upload.AttachmentId, attachment.Id);
        Assert.Equal("inspection.txt", attachment.FileName);
        Assert.Equal(bytes.Length, attachment.SizeBytes);
        var serialized = JsonSerializer.Serialize(ready, Json);
        foreach (var forbidden in new[] { "uploads/", "X-Amz-", "storageKey", upload.Url, "Private inspection attachment bytes" })
        {
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(auditBefore, await admin.GetStringAsync(auditPath));
    }

    private static InspectionHost CreateHost(bool notesEnabled = false, IReadOnlyDictionary<string, string>? configuration = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var settings = new Dictionary<string, string>
        {
            ["FeatureManagement:Mcp"] = "true",
            ["Mcp:AllowLocalhostHttp"] = "true",
            ["Ngb:Hosting:PublicApiUrl"] = $"http://127.0.0.1:{port}"
        };
        if (configuration is not null)
        {
            foreach (var (key, value) in configuration)
            {
                settings.Add(key, value);
            }
        }
        return new InspectionHost(notesEnabled: notesEnabled, configuration: settings, port: port,
            configureServices: services => services.AddSingleton<IMcpNgbLinkResolver>(
            new ConfiguredMcpNgbLinkResolver(new Uri("https://inspection.example.test"), new Dictionary<string, string>
            {
                ["catalog"] = "/catalogs/{typeCode}/{id}",
                ["document"] = "/documents/{typeCode}/{id}",
                ["report"] = "/reports/{typeCode}"
            })));
    }

    private static async Task<McpReadResponse<T>> ReadAsync<T>(HttpClient client, string name, object arguments)
    {
        var result = await CallAsync(client, name, arguments);
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.GetRawText());
        var value = result.GetProperty("structuredContent").Deserialize<McpReadResponse<T>>(Json)!;
        Assert.Equal(1, value.SchemaVersion);
        Assert.Equal(DateTimeKind.Utc, value.GeneratedAtUtc.Kind);
        return value;
    }

    private static async Task<string?> ErrorAsync(HttpClient client, string name, object arguments)
    {
        var result = await CallAsync(client, name, arguments);
        Assert.True(result.GetProperty("isError").GetBoolean());
        var text = Assert.Single(result.GetProperty("content").EnumerateArray()).GetProperty("text").GetString();
        return JsonSerializer.Deserialize<McpReadError>(text!, Json)!.Code;
    }

    private static async Task<FetchResult> FetchAsync(HttpClient client, string id)
    {
        var result = await CallAsync(client, McpToolNames.CompatibilityFetch, new { id });
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.GetRawText());
        var content = result.GetProperty("structuredContent");
        Assert.False(content.TryGetProperty("schemaVersion", out _));
        return content.Deserialize<FetchResult>(Json)!;
    }

    private static async Task<JsonElement> CallAsync(HttpClient client, string name, object arguments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments } }, options: Json)
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        using var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        var messages = response.Content.Headers.ContentType!.MediaType == "text/event-stream"
            ? body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..])
            : [body];
        var message = Assert.Single(messages.Select(value => JsonSerializer.Deserialize<JsonElement>(value)),
            value => value.TryGetProperty("id", out var id) && id.GetInt32() == 1);
        Assert.False(message.TryGetProperty("error", out _), message.GetRawText());
        return message.GetProperty("result");
    }
}
