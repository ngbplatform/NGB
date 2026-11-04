using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Persistence.AuditLog;
using NGB.PostgreSql.AuditLog;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.ReferenceRegisters;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class EquipmentConditionHttpTests
{
    private static Guid RegisterId => ReferenceRegisterId.FromCode(EquipmentConditionDefinitions.RegisterCode);

    [Fact]
    public async Task Assessment_versions_tombstones_and_historical_reads_preserve_original_records()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var equipment = await CreateEquipmentAsync(admin, "Assessment bench");
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentConditionDefinitions.DocumentCode, new RecordPayload());
        var path = Path(draft.Id);
        var state = await StateAsync(admin, path);
        using var incomplete = await ExecutePostAsync(admin, path, state.DocumentVersion, "incomplete-assessment");
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        await AssertNoEffectsAsync(admin, path);

        using var updated = await admin.PutAsJsonAsync(path, Payload(("equipment_id", equipment.Id), ("condition", "Ready")));
        await EnsureSuccessAsync(updated);
        state = await StateAsync(admin, path);
        using var denied = await ExecutePostAsync(reader, path, state.DocumentVersion, "denied-assessment");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var postedResponse = await ExecutePostAsync(admin, path, state.DocumentVersion, "post-assessment");
        var posted = await ReadActionAsync(postedResponse);

        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var key = Dimensions(equipment.Id);
        var original = await reads.SliceLastAsync(RegisterId, key, DateTime.UtcNow, draft.Id);
        Assert.NotNull(original);
        Assert.Equal("Ready", original.Values["condition"]);
        Assert.Null(original.PeriodUtc);
        Assert.False(original.IsDeleted);
        var effects = await admin.GetFromJsonAsync<DocumentEffectsDto>(path + "/effects", Json);
        var effect = Assert.Single(effects!.ReferenceRegisterWrites);
        Assert.Equal(draft.Id, effect.DocumentId);
        Assert.Equal(original.DimensionSetId, effect.DimensionSetId);
        Assert.Equal("Ready", effect.Fields["condition"].GetString());
        Assert.Equal("Assessment bench", Assert.Single(effect.Dimensions).Display);
        Assert.Empty(effects.AccountingEntries);
        Assert.Empty(effects.OperationalRegisterMovements);
        using var deniedEffects = await reader.GetAsync(path + "/effects");
        Assert.Equal(HttpStatusCode.Forbidden, deniedEffects.StatusCode);

        var defaults = ActivatorUtilities.CreateInstance<EquipmentConditionDefaults>(scope.ServiceProvider);
        await defaults.ApplyAsync(CancellationToken.None);
        var management = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        await Assert.ThrowsAsync<ReferenceRegisterMetadataImmutabilityViolationException>(() => management.UpsertAsync(
            EquipmentConditionDefinitions.RegisterCode, "Equipment assessment records",
            ReferenceRegisterPeriodicity.Day, ReferenceRegisterRecordMode.SubordinateToRecorder));
        await Assert.ThrowsAsync<ReferenceRegisterDimensionRulesAppendOnlyViolationException>(() =>
            management.ReplaceDimensionRulesAsync(RegisterId, []));
        Assert.Equal(original.RecordId, (await reads.SliceLastAsync(RegisterId, key, DateTime.UtcNow, draft.Id))!.RecordId);

        using var retry = await ExecutePostAsync(admin, path, state.DocumentVersion, "post-assessment");
        Assert.Equal(posted.ExecutionId, (await ReadActionAsync(retry)).ExecutionId);
        Assert.Single(await HistoryAsync(reads, equipment.Id, draft.Id));
        using var repostResponse = await ExecuteActionAsync(admin, path, "repost", posted.DocumentVersion, "repost-assessment");
        var reposted = await ReadActionAsync(repostResponse);
        Assert.Equal(DocumentStatus.Posted, reposted.Document.Status);
        var afterRepost = await HistoryAsync(reads, equipment.Id, draft.Id);
        Assert.Equal(2, afterRepost.Count);
        Assert.Equal(original.RecordId, afterRepost[^1].RecordId);

        using var unpostResponse = await ExecuteActionAsync(admin, path, "unpost", reposted.DocumentVersion, "unpost-assessment");
        Assert.Equal(DocumentStatus.Draft, (await ReadActionAsync(unpostResponse)).Document.Status);
        await AssertNoEffectsAsync(admin, path);
        Assert.Null(await reads.SliceLastAsync(RegisterId, key, DateTime.UtcNow, draft.Id));
        var tombstone = await reads.SliceLastAsync(RegisterId, key, DateTime.UtcNow, draft.Id, includeDeleted: true);
        Assert.True(tombstone!.IsDeleted);
        Assert.Equal("Ready", tombstone.Values["condition"]);
        var history = await HistoryAsync(reads, equipment.Id, draft.Id);
        Assert.Equal(3, history.Count);
        Assert.Equal(afterRepost.Select(row => row.RecordId), history.Skip(1).Select(row => row.RecordId));
        var historical = await reads.SliceLastAsync(RegisterId, key, original.RecordedAtUtc, draft.Id);
        Assert.Equal(original.RecordId, historical!.RecordId);
        Assert.Null(await reads.SliceLastAsync(RegisterId, key, original.RecordedAtUtc.AddTicks(-10), draft.Id));

        var replacement = await CreateEquipmentAsync(admin, "Replacement bench");
        using var changed = await admin.PutAsJsonAsync(path, Payload(("equipment_id", replacement.Id), ("condition", "Needs service")));
        await EnsureSuccessAsync(changed);
        state = await StateAsync(admin, path);
        using var newPost = await ExecutePostAsync(admin, path, state.DocumentVersion, "replacement-assessment");
        await EnsureSuccessAsync(newPost);
        var visible = await ReadAllAsync(reads, draft.Id, includeDeleted: false);
        Assert.Equal("Needs service", Assert.Single(visible).Values["condition"]);
        var allKeys = await ReadAllAsync(reads, draft.Id, includeDeleted: true);
        Assert.Equal(2, allKeys.Count);
        Assert.Single(allKeys, row => row.IsDeleted);
        var filtered = await reads.SliceLastAllEnrichedPageAsync(RegisterId, DateTime.UtcNow,
            Dimensions(replacement.Id), draft.Id, limit: 1);
        var snapshot = Assert.Single(filtered.Records);
        Assert.Equal(replacement.Id, Assert.Single(snapshot.Dimensions).ValueId);
        Assert.Equal("Replacement bench", snapshot.DimensionValueDisplaysByDimensionId[EquipmentDefinitions.EquipmentDimensionId]);
        var deletedFilter = await reads.SliceLastAllFilteredPageAsync(RegisterId, DateTime.UtcNow, key, draft.Id, limit: 1);
        Assert.Empty(deletedFilter.Records);
        Assert.False(deletedFilter.HasMore);
    }

    [Fact]
    public async Task Concurrent_posts_commit_once_and_unposting_one_recorder_preserves_another()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await CreateEquipmentAsync(admin, "Shared bench");
        var first = await CreateAssessmentAsync(admin, equipment.Id, "First assessment");
        var second = await CreateAssessmentAsync(admin, equipment.Id, "Second assessment");
        var state = await StateAsync(admin, Path(first.Id));
        var responses = await Task.WhenAll(
            ExecutePostAsync(admin, Path(first.Id), state.DocumentVersion, "concurrent-assessment-a"),
            ExecutePostAsync(admin, Path(first.Id), state.DocumentVersion, "concurrent-assessment-b"));
        using var responseA = responses[0];
        using var responseB = responses[1];
        var posted = await ReadActionAsync(Assert.Single(responses, response => response.IsSuccessStatusCode));
        Assert.Equal(HttpStatusCode.Conflict, Assert.Single(responses, response => !response.IsSuccessStatusCode).StatusCode);
        var secondState = await StateAsync(admin, Path(second.Id));
        using var secondPost = await ExecutePostAsync(admin, Path(second.Id), secondState.DocumentVersion, "second-assessment");
        await EnsureSuccessAsync(secondPost);

        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        Assert.Single(await HistoryAsync(reads, equipment.Id, first.Id));
        using var unpost = await ExecuteActionAsync(admin, Path(first.Id), "unpost", posted.DocumentVersion, "unpost-first-assessment");
        await EnsureSuccessAsync(unpost);
        Assert.Null(await reads.SliceLastAsync(RegisterId, Dimensions(equipment.Id), DateTime.UtcNow, first.Id));
        var remaining = await reads.SliceLastAsync(RegisterId, Dimensions(equipment.Id), DateTime.UtcNow, second.Id);
        Assert.Equal("Second assessment", remaining!.Values["condition"]);
        Assert.Single(await HistoryAsync(reads, equipment.Id, second.Id));
    }

    [Fact]
    public async Task Failure_after_a_reference_write_rolls_back_records_document_and_action_state()
    {
        var failure = new AuditFailure();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<PostgresAuditEventWriter>();
            services.AddScoped<IAuditEventWriter>(provider => new FailingAuditWriter(
                provider.GetRequiredService<PostgresAuditEventWriter>(),
                provider.GetRequiredService<IReferenceRegisterReadService>(), failure));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await CreateEquipmentAsync(admin, "Rollback assessment bench");
        var draft = await CreateAssessmentAsync(admin, equipment.Id, "Ready");
        var path = Path(draft.Id);
        var before = await StateAsync(admin, path);
        using var failed = await ExecutePostAsync(admin, path, before.DocumentVersion, "rollback-assessment");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(failure.SawRecord);
        var after = await StateAsync(admin, path);
        Assert.Equal(DocumentStatus.Draft, after.Document.Status);
        Assert.Equal(before.DocumentVersion, after.DocumentVersion);
        await AssertNoEffectsAsync(admin, path);
        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        Assert.Empty(await HistoryAsync(reads, equipment.Id, draft.Id));

        failure.Enabled = false;
        using var retry = await ExecutePostAsync(admin, path, before.DocumentVersion, "rollback-assessment");
        Assert.Equal(DocumentStatus.Posted, (await ReadActionAsync(retry)).Document.Status);
        Assert.Single(await HistoryAsync(reads, equipment.Id, draft.Id));
    }

    private static string Path(Guid id) => "/api/documents/" + EquipmentConditionDefinitions.DocumentCode + "/" + id;

    private static IReadOnlyList<DimensionValue> Dimensions(Guid equipment)
        => [new(EquipmentDefinitions.EquipmentDimensionId, equipment)];

    private static Task<CatalogItemDto> CreateEquipmentAsync(HttpClient client, string display)
        => PostAsync<CatalogItemDto>(client, "/api/catalogs/example.equipment", Payload(("display", display)));

    private static Task<DocumentDto> CreateAssessmentAsync(HttpClient client, Guid equipment, string condition)
        => PostAsync<DocumentDto>(client, "/api/documents/" + EquipmentConditionDefinitions.DocumentCode,
            Payload(("equipment_id", equipment), ("condition", condition)));

    private static async Task<DocumentEditorStateDto> StateAsync(HttpClient client, string path)
        => (await client.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;

    private static async Task<List<ReferenceRegisterRecordRead>> HistoryAsync(IReferenceRegisterReadService reads, Guid equipment, Guid document)
    {
        var rows = new List<ReferenceRegisterRecordRead>();
        var asOf = DateTime.UtcNow;
        DateTime? beforeTime = null;
        long? beforeId = null;
        while (true)
        {
            var page = await reads.GetKeyHistoryAsync(RegisterId, Dimensions(equipment), asOf,
                recorderDocumentId: document, beforeRecordedAtUtc: beforeTime, beforeRecordId: beforeId,
                limit: 1, includeDeleted: true);
            if (page.Count == 0)
                break;

            var row = Assert.Single(page);
            rows.Add(row);
            Assert.InRange(rows.Count, 1, 10);
            beforeTime = row.RecordedAtUtc;
            beforeId = row.RecordId;
        }

        Assert.Equal(rows.Count, rows.Select(row => row.RecordId).Distinct().Count());
        return rows;
    }

    private static async Task<List<ReferenceRegisterRecordRead>> ReadAllAsync(IReferenceRegisterReadService reads, Guid document, bool includeDeleted)
    {
        var rows = new List<ReferenceRegisterRecordRead>();
        var cursors = new HashSet<Guid>();
        var asOf = DateTime.UtcNow;
        Guid? cursor = null;
        ReferenceRegisterSlicePage<ReferenceRegisterRecordRead> page;
        do
        {
            page = await reads.SliceLastAllPageAsync(RegisterId, asOf, document, cursor, limit: 1, includeDeleted: includeDeleted);
            Assert.InRange(page.Records.Count, 0, 1);
            rows.AddRange(page.Records);
            cursor = page.NextAfterDimensionSetId;
            if (page.HasMore)
            {
                Assert.NotNull(cursor);
                Assert.True(cursors.Add(cursor.Value));
            }
            Assert.InRange(rows.Count, 0, 2);
        } while (page.HasMore);

        Assert.Equal(rows.Count, rows.Select(row => row.DimensionSetId).Distinct().Count());
        return rows;
    }

    private sealed class AuditFailure
    {
        public bool Enabled { get; set; } = true;
        public bool SawRecord { get; set; }
    }

    private sealed class FailingAuditWriter(
        PostgresAuditEventWriter inner,
        IReferenceRegisterReadService reads,
        AuditFailure failure) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            if (failure.Enabled && auditEvent.ActionCode == "document.post")
            {
                var rows = await reads.SliceLastAllAsync(RegisterId, DateTime.UtcNow,
                    recorderDocumentId: auditEvent.EntityId, ct: ct);
                failure.SawRecord = rows.Count == 1;
                throw new InvalidOperationException("Injected failure after the reference record was written.");
            }
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            foreach (var auditEvent in auditEvents)
                await WriteAsync(auditEvent, ct);
        }
    }
}
