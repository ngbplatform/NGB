using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ChecklistHttpTests
{
    private const string DocumentsPath = "/api/documents/" + ChecklistDefinitions.DocumentCode;

    [Fact]
    public async Task Head_and_parts_round_trip_across_batches_and_omitted_parts_are_preserved_until_explicitly_cleared()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var rows = Enumerable.Range(1, 501).Select(index => Row(index, "Check " + index, index % 2 == 0)).ToArray();
        var draft = await PostAsync<DocumentDto>(admin, DocumentsPath, WithChecks(rows));
        var path = DocumentsPath + "/" + draft.Id;
        Assert.Equal(DocumentStatus.Draft, draft.Status);
        Assert.Equal(501, draft.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows.Count);
        Assert.Equal(Enumerable.Range(1, 501), draft.Payload.Parts[ChecklistDefinitions.ChecksPartCode].Rows
            .Select(row => row["line_no"].GetInt32()).Order());
        var persisted = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        Assert.Equal(JsonSerializer.Serialize(draft.Payload, Json), JsonSerializer.Serialize(persisted.Payload, Json));

        using var renamed = await admin.PutAsJsonAsync(path, Payload(("display", "Reviewed equipment checks")));
        await EnsureSuccessAsync(renamed);
        var retained = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        Assert.Equal("Reviewed equipment checks", retained.Display);
        Assert.Equal(501, retained.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows.Count);
        using var replaced = await admin.PutAsJsonAsync(path, WithChecks([Row(7, null, null)]));
        await EnsureSuccessAsync(replaced);
        var incomplete = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        Assert.Equal(7, Assert.Single(incomplete.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows)["line_no"].GetInt32());
        using var cleared = await admin.PutAsJsonAsync(path, WithChecks([]));
        await EnsureSuccessAsync(cleared);
        Assert.Empty((await admin.GetFromJsonAsync<DocumentDto>(path, Json))!.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows);
    }

    [Fact]
    public async Task Invalid_payloads_preserve_the_existing_head_parts_audit_and_document_count()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var draft = await PostAsync<DocumentDto>(admin, DocumentsPath, WithChecks([Row(1, "Guard", true)]));
        var path = DocumentsPath + "/" + draft.Id;
        var before = await admin.GetStringAsync(path);
        var audit = await admin.GetStringAsync($"/api/audit/entities/Document/{draft.Id}");
        RecordPayload[] invalid =
        [
            WithChecks([Row(0, null, null)]),
            WithChecks([Row(1, "First", true), Row(1, "Duplicate", false)]),
            WithChecks([new Dictionary<string, JsonElement>()]),
            WithChecks([new Dictionary<string, JsonElement>
            {
                ["line_no"] = JsonSerializer.SerializeToElement(1),
                ["document_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7())
            }]),
            WithChecks([new Dictionary<string, JsonElement>
            {
                ["line_no"] = JsonSerializer.SerializeToElement(1),
                ["unknown"] = JsonSerializer.SerializeToElement("value")
            }]),
            WithChecks([new Dictionary<string, JsonElement>
            {
                ["line_no"] = JsonSerializer.SerializeToElement("not a number")
            }]),
            new(Parts: new Dictionary<string, RecordPartPayload> { ["unknown"] = new([Row(1, "Check", true)]) })
        ];
        foreach (var payload in invalid)
        {
            using var update = await admin.PutAsJsonAsync(path, payload);
            Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
            using var create = await admin.PostAsJsonAsync(DocumentsPath, payload);
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
            Assert.Equal(before, await admin.GetStringAsync(path));
            Assert.Equal(audit, await admin.GetStringAsync($"/api/audit/entities/Document/{draft.Id}"));
        }
        var page = (await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(DocumentsPath + "?includeTotal=true", Json))!;
        Assert.Equal(1, page.Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Posting_requires_complete_checks_and_persisted_review_for_both_administrator_kinds(bool linked)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (linked)
        {
            await host.LinkUserAsync("administrator", true, InspectionHost.AdministratorRoleCode, "Renamed supervisor");
        }
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var draft = await PostAsync<DocumentDto>(admin, DocumentsPath, new RecordPayload());
        var path = DocumentsPath + "/" + draft.Id;
        using var reviewedEmpty = await admin.PutAsJsonAsync(path, Payload(("reviewed", true)));
        await EnsureSuccessAsync(reviewedEmpty);
        var state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var emptyPost = await ExecutePostAsync(admin, path, state.DocumentVersion, "empty-checklist");
        Assert.Equal(HttpStatusCode.BadRequest, emptyPost.StatusCode);
        Assert.Contains("Complete at least one check", await emptyPost.Content.ReadAsStringAsync());

        using var incomplete = await admin.PutAsJsonAsync(path, WithChecks([Row(1, null, null)]));
        await EnsureSuccessAsync(incomplete);
        state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var incompletePost = await ExecutePostAsync(admin, path, state.DocumentVersion, "incomplete-checklist");
        Assert.Equal(HttpStatusCode.BadRequest, incompletePost.StatusCode);
        using var complete = await admin.PutAsJsonAsync(path, WithChecks([Row(1, "Guard present", true), Row(2, "Cable intact", false)])
            with { Fields = Payload(("reviewed", false)).Fields });
        await EnsureSuccessAsync(complete);
        state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var unreviewedPost = await ExecutePostAsync(admin, path, state.DocumentVersion, "unreviewed-checklist");
        Assert.Equal(HttpStatusCode.BadRequest, unreviewedPost.StatusCode);
        Assert.Contains("Review the checklist", await unreviewedPost.Content.ReadAsStringAsync());
        await AssertNoEffectsAsync(admin, path);

        using var deniedEdit = await reader.PutAsJsonAsync(path, Payload(("reviewed", true)));
        Assert.Equal(HttpStatusCode.Forbidden, deniedEdit.StatusCode);
        using var deniedPost = await ExecutePostAsync(reader, path, state.DocumentVersion, "denied-checklist");
        Assert.Equal(HttpStatusCode.Forbidden, deniedPost.StatusCode);
        using var reviewed = await admin.PutAsJsonAsync(path, Payload(("reviewed", true)));
        await EnsureSuccessAsync(reviewed);
        state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var postedResponse = await ExecutePostAsync(admin, path, state.DocumentVersion, "reviewed-checklist");
        var posted = await ReadActionAsync(postedResponse);
        Assert.Equal(DocumentStatus.Posted, posted.Document.Status);
        Assert.Equal(2, posted.Document.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows.Count);
        await AssertNoEffectsAsync(admin, path);
        using var postedEdit = await admin.PutAsJsonAsync(path, WithChecks([]));
        Assert.False(postedEdit.IsSuccessStatusCode);
        using var unposted = await ExecuteActionAsync(admin, path, "unpost", posted.DocumentVersion, "unpost-checklist");
        Assert.Equal(DocumentStatus.Draft, (await ReadActionAsync(unposted)).Document.Status);
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Failure_after_part_and_audit_writes_rolls_back_both_create_and_update()
    {
        var failure = new AuditFailure();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<PostgresAuditEventWriter>();
            services.AddScoped<IAuditEventWriter>(provider => new FailingAuditWriter(
                provider.GetRequiredService<PostgresAuditEventWriter>(), provider.GetRequiredService<IChecklistReader>(), failure));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var original = await PostAsync<DocumentDto>(admin, DocumentsPath, WithChecks([Row(1, "Original", true)]));
        var path = DocumentsPath + "/" + original.Id;
        var before = await admin.GetStringAsync(path);
        var audit = await admin.GetStringAsync($"/api/audit/entities/Document/{original.Id}");
        var changed = WithChecks(Enumerable.Range(1, 501).Select(index => Row(index, "Replacement " + index, true)).ToArray())
            with { Fields = Payload(("display", "Replacement head")).Fields };
        failure.Enabled = true;
        using var update = await admin.PutAsJsonAsync(path, changed);
        Assert.Equal(HttpStatusCode.InternalServerError, update.StatusCode);
        Assert.Equal(501, failure.ObservedLines);
        Assert.Equal(before, await admin.GetStringAsync(path));
        Assert.Equal(audit, await admin.GetStringAsync($"/api/audit/entities/Document/{original.Id}"));
        using var create = await admin.PostAsJsonAsync(DocumentsPath, changed);
        Assert.Equal(HttpStatusCode.InternalServerError, create.StatusCode);
        Assert.Equal(501, failure.ObservedLines);
        Assert.NotEqual(original.Id, failure.DocumentId);
        using var absent = await admin.GetAsync(DocumentsPath + "/" + failure.DocumentId);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var absentAudit = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/entities/Document/{failure.DocumentId}");
        Assert.Empty(absentAudit.GetProperty("items").EnumerateArray());
        failure.Enabled = false;
        using var retry = await admin.PutAsJsonAsync(path, changed);
        await EnsureSuccessAsync(retry);
        Assert.Equal(501, (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows.Count);
    }

    [Fact]
    public async Task Common_draft_validation_and_typed_storage_preserve_transactions_and_delete_owned_parts()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var draft = await PostAsync<DocumentDto>(admin, DocumentsPath, WithChecks([Row(1, null, null)]));
        var path = DocumentsPath + "/" + draft.Id;
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var drafts = services.GetRequiredService<IDocumentDraftService>();
        var documents = services.GetRequiredService<IDocumentRepository>();
        var uow = services.GetRequiredService<IUnitOfWork>();
        var storage = services.GetRequiredService<IDocumentTypeStorageResolver>().TryResolve(ChecklistDefinitions.DocumentCode);
        Assert.IsType<ChecklistStorage>(storage);
        var before = (await documents.GetAsync(draft.Id))!;
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => drafts.UpdateDraftAsync(
            draft.Id, null, DateTime.UtcNow.Date.AddDays(2)));
        Assert.Equal(before.DateUtc, (await documents.GetAsync(draft.Id))!.DateUtc);
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => drafts.CreateDraftAsync(
            ChecklistDefinitions.DocumentCode, null, DateTime.UtcNow.Date.AddDays(2)));
        Assert.False(uow.HasActiveTransaction);

        var rolledBack = Guid.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInUowTransactionAsync(async ct =>
        {
            rolledBack = await drafts.CreateDraftAsync(ChecklistDefinitions.DocumentCode, null, DateTime.UtcNow,
                manageTransaction: false, ct: ct);
            await storage!.CreateDraftAsync(rolledBack, ct);
            Assert.NotNull(await services.GetRequiredService<IChecklistReader>().ReadAsync(rolledBack, ct));
            throw new InvalidOperationException("Injected failure after typed checklist creation.");
        }));
        Assert.Null(await documents.GetAsync(rolledBack));
        using var deleted = await admin.DeleteAsync(path);
        await EnsureSuccessAsync(deleted);
        Assert.Null(await documents.GetAsync(draft.Id));
        var definition = services.GetRequiredService<DefinitionsRegistry>().GetDocument(ChecklistDefinitions.DocumentCode);
        var parts = definition.Metadata.Tables.Where(table => table.Kind == TableKind.Part).ToArray();
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            Assert.Null(await services.GetRequiredService<IChecklistReader>().ReadAsync(draft.Id, ct));
            var remaining = await services.GetRequiredService<IDocumentPartsReader>().GetPartsAsync(parts, draft.Id, ct);
            Assert.All(remaining.Values, rows => Assert.Empty(rows));
        });
    }

    private static IReadOnlyDictionary<string, JsonElement> Row(int line, string? description, bool? passed)
    {
        return new Dictionary<string, JsonElement>
        {
            ["line_no"] = JsonSerializer.SerializeToElement(line),
            ["description"] = JsonSerializer.SerializeToElement(description),
            ["passed"] = JsonSerializer.SerializeToElement(passed)
        };
    }

    private static RecordPayload WithChecks(IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows)
    {
        return new RecordPayload(Parts: new Dictionary<string, RecordPartPayload>
        {
            [ChecklistDefinitions.ChecksPartCode] = new(rows)
        });
    }

    private sealed class AuditFailure
    {
        public bool Enabled { get; set; }
        public long ObservedLines { get; set; }
        public Guid DocumentId { get; set; }
    }

    private sealed class FailingAuditWriter(
        PostgresAuditEventWriter inner,
        IChecklistReader reader,
        AuditFailure failure) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            if (failure.Enabled && auditEvent.ActionCode is AuditActionCodes.DocumentCreateDraft or AuditActionCodes.DocumentUpdateDraft)
            {
                var checklist = await reader.ReadAsync(auditEvent.EntityId, ct);
                failure.DocumentId = auditEvent.EntityId;
                failure.ObservedLines = checklist!.LineCount;
                throw new InvalidOperationException("Injected failure after checklist head, parts and audit writes.");
            }
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            foreach (var auditEvent in auditEvents)
            {
                await WriteAsync(auditEvent, ct);
            }
        }
    }
}
