using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.PostgreSql;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Definitions.Documents.Actions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;
using CoreStatus = NGB.Core.Documents.DocumentStatus;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionDeferralHttpTests
{
    [Fact]
    public async Task Custom_action_advances_once_requires_a_reason_and_loads_one_fact_set_per_state()
    {
        var reads = new ConcurrentQueue<Guid>();
        await using var host = new InspectionHost(configureServices: services =>
            services.AddScoped<IInspectionReader>(provider => new CountingReader(
                ActivatorUtilities.CreateInstance<InspectionReader>(provider), reads)));
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode, "Renamed inspection supervisors");
        using var client = await host.CreateClientAsync("application-administrator");
        var (path, version) = await CreateCompleteInspectionAsync(client);
        var before = await RecordAsync(host, path);
        reads.Clear();
        var state = await client.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.True(Assert.Single(state!.Actions, action => action.Code == InspectionDeferralDefinitions.ActionCode).IsAllowed);
        Assert.Equal(before.Id, Assert.Single(reads));

        using var missingReason = await DeferAsync(client, path, version, "missing-reason", null);
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal(before.DateUtc, (await RecordAsync(host, path)).DateUtc);
        Assert.Empty(await ActionsAsync(host, before.Id));
        reads.Clear();
        using var deferred = await DeferAsync(client, path, version, "one-deferral", "Equipment unavailable");
        var result = await ReadActionAsync(deferred);
        Assert.Equal(2, reads.Count);
        Assert.All(reads, id => Assert.Equal(before.Id, id));
        Assert.Equal(version + 1, result.DocumentVersion);
        Assert.Equal(NGB.Contracts.Metadata.DocumentStatus.Draft, result.Document.Status);
        var after = await RecordAsync(host, path);
        Assert.Equal(before.DateUtc.AddDays(1), after.DateUtc);
        Assert.Equal(before.Number, after.Number);
        var audit = Assert.Single(await ActionsAsync(host, before.Id));
        Assert.Equal(result.ExecutionId, audit.CorrelationId);
        Assert.NotNull(audit.ActorUserId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Equipment unavailable", metadata.RootElement.GetProperty("reason").GetString());

        reads.Clear();
        using var repeated = await DeferAsync(client, path, version, "one-deferral", "Equipment unavailable");
        Assert.Equal(result.ExecutionId, (await ReadActionAsync(repeated)).ExecutionId);
        Assert.Equal(before.Id, Assert.Single(reads));
        Assert.Equal(after.DateUtc, (await RecordAsync(host, path)).DateUtc);
        Assert.Single(await ActionsAsync(host, before.Id));
        using var stale = await DeferAsync(client, path, version, "stale-deferral", "Another request");
        await AssertErrorAsync(stale, HttpStatusCode.Conflict, "document.version_conflict");
        await AssertNoEffectsAsync(client, path);
    }

    [Fact]
    public async Task View_permission_does_not_authorize_mutation_and_business_availability_cannot_be_bypassed()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "example.inspection-viewer", "Inspection viewer");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var role = await services.GetRequiredService<IPlatformRoleRepository>().GetByCodeAsync("example.inspection-viewer");
            await services.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(ct =>
                services.GetRequiredService<IPermissionSnapshotRepository>().ReplaceRolePermissionsAsync(role!.RoleId,
                    [new NgbPermissionKey(NgbResourceKinds.Document, EquipmentDefinitions.InspectionCode, NgbPermissionActions.View)], ct));
        }
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var (path, version) = await CreateCompleteInspectionAsync(admin);
        var before = await RecordAsync(host, path);
        var visible = await reader.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.Equal(before.Id, visible!.Document.Id);
        Assert.DoesNotContain(visible.Actions, action => action.Code == InspectionDeferralDefinitions.ActionCode);
        using var denied = await DeferAsync(reader, path, version, "viewer-deferral", "Not authorized");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before.DateUtc, (await RecordAsync(host, path)).DateUtc);
        Assert.Empty(await ActionsAsync(host, before.Id));

        var incomplete = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentDefinitions.InspectionCode, Payload());
        var incompletePath = "/api/documents/" + EquipmentDefinitions.InspectionCode + "/" + incomplete.Id;
        var incompleteState = await admin.GetFromJsonAsync<DocumentEditorStateDto>(incompletePath + "/editor-state", Json);
        var disabled = Assert.Single(incompleteState!.Actions, action => action.Code == InspectionDeferralDefinitions.ActionCode);
        Assert.False(disabled.IsAllowed);
        Assert.Equal("inspection.deferral.equipment-required", Assert.Single(disabled.DisabledReasons).Code);
        using var bypass = await DeferAsync(admin, incompletePath, incompleteState.DocumentVersion, "incomplete-deferral", "Equipment missing");
        await AssertErrorAsync(bypass, HttpStatusCode.Conflict, "document_action.unavailable");

        using var posted = await ExecutePostAsync(admin, path, version, "post-before-deferral");
        var postedState = await ReadActionAsync(posted);
        disabled = Assert.Single(postedState.Actions, action => action.Code == InspectionDeferralDefinitions.ActionCode);
        Assert.False(disabled.IsAllowed);
        Assert.Contains(disabled.DisabledReasons, reason => reason.Code == "inspection.deferral.not-draft");
        using var postedBypass = await DeferAsync(admin, path, postedState.DocumentVersion, "posted-deferral", "Too late");
        await AssertErrorAsync(postedBypass, HttpStatusCode.Conflict, "document_action.unavailable");
        Assert.Equal(CoreStatus.Posted, (await RecordAsync(host, path)).Status);
        Assert.Equal(before.DateUtc, (await RecordAsync(host, path)).DateUtc);
    }

    [Fact]
    public async Task Concurrent_commands_with_one_expected_version_change_the_date_only_once()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CreateCompleteInspectionAsync(admin);
        var before = await RecordAsync(host, path);
        var responses = await Task.WhenAll(
            DeferAsync(admin, path, version, "concurrent-deferral-a", "First scheduler"),
            DeferAsync(admin, path, version, "concurrent-deferral-b", "Second scheduler"));
        using var first = responses[0];
        using var second = responses[1];
        var winner = await ReadActionAsync(Assert.Single(responses, response => response.IsSuccessStatusCode));
        await AssertErrorAsync(Assert.Single(responses, response => !response.IsSuccessStatusCode),
            HttpStatusCode.Conflict, "document.version_conflict");
        var current = await RecordAsync(host, path);
        Assert.Equal(version + 1, current.Version);
        Assert.Equal(winner.DocumentVersion, current.Version);
        Assert.Equal(before.DateUtc.AddDays(1), current.DateUtc);
        Assert.Single(await ActionsAsync(host, before.Id));
    }

    [Fact]
    public async Task Handler_failure_rolls_back_the_changed_date_audit_and_execution_for_an_exact_retry()
    {
        var failure = new MutationFailure();
        await using var host = new InspectionHost(configureServices: services =>
        {
            var definition = services.Single(item => item.ImplementationType == typeof(InspectionDeferralDefinitions));
            services.Remove(definition);
            services.AddSingleton<IDocumentActionDefinitionsContributor, FailingDefinition>();
            services.AddSingleton(failure);
            services.AddScoped<FailingHandler>();
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, version) = await CreateCompleteInspectionAsync(admin);
        var before = await RecordAsync(host, path);
        using var failed = await DeferAsync(admin, path, version, "rollback-deferral", "Equipment unavailable");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(before.DateUtc.AddDays(1), failure.ObservedDate);
        Assert.Equal(before.DateUtc, (await RecordAsync(host, path)).DateUtc);
        Assert.Equal(before.Version, (await RecordAsync(host, path)).Version);
        Assert.Empty(await ActionsAsync(host, before.Id));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var updates = await scope.ServiceProvider.GetRequiredService<IAuditEventReader>().QueryAsync(
                new AuditLogQuery(EntityKind: AuditEntityKind.Document, EntityId: before.Id, ActionCode: AuditActionCodes.DocumentUpdateDraft));
            Assert.Empty(updates);
        }

        failure.Enabled = false;
        using var retry = await DeferAsync(admin, path, version, "rollback-deferral", "Equipment unavailable");
        var result = await ReadActionAsync(retry);
        Assert.Equal(version + 1, result.DocumentVersion);
        Assert.Equal(before.DateUtc.AddDays(1), (await RecordAsync(host, path)).DateUtc);
        Assert.Single(await ActionsAsync(host, before.Id));
    }

    private static async Task<HttpResponseMessage> DeferAsync(HttpClient client, string path, long version, string key, string? reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/actions/" + InspectionDeferralDefinitions.ActionCode)
        {
            Content = JsonContent.Create(new ExecuteDocumentActionRequestDto(version, Reason: reason))
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<NGB.Core.Documents.DocumentRecord> RecordAsync(InspectionHost host, string path)
    {
        var id = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().GetAsync(id)
            ?? throw new InvalidOperationException("Missing fixture document.");
    }

    private static async Task<IReadOnlyList<AuditEvent>> ActionsAsync(InspectionHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditEventReader>().QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Document, EntityId: id, ActionCode: "document.action." + InspectionDeferralDefinitions.ActionCode));
    }

    private sealed class CountingReader(InspectionReader inner, ConcurrentQueue<Guid> reads) : IInspectionReader
    {
        public Task<InspectionDetails?> ReadAsync(Guid documentId, CancellationToken ct)
        {
            reads.Enqueue(documentId);
            return inner.ReadAsync(documentId, ct);
        }
    }

    private sealed class MutationFailure
    {
        public bool Enabled { get; set; } = true;
        public DateTime? ObservedDate { get; set; }
    }

    private sealed class FailingDefinition : IDocumentActionDefinitionsContributor
    {
        public void Contribute(DocumentActionDefinitionsBuilder builder)
        {
            var original = new DocumentActionDefinitionsBuilder();
            new InspectionDeferralDefinitions().Contribute(original);
            var definition = Assert.Single(original.Build());
            builder.Add(definition.DocumentTypeCode, definition.Metadata, typeof(FailingHandler),
                definition.AvailabilityEvaluatorType, definition.AuthorizationEvaluatorType);
        }
    }

    private sealed class FailingHandler(InspectionDeferralHandler inner, IDocumentRepository documents, MutationFailure failure) : IDocumentActionHandler
    {
        public async Task<DocumentActionHandlerResult> ExecuteAsync(DocumentActionHandlerContext context, CancellationToken ct)
        {
            var result = await inner.ExecuteAsync(context, ct);
            failure.ObservedDate = (await documents.GetAsync(context.Document.Id, ct))!.DateUtc;
            if (failure.Enabled)
                throw new InvalidOperationException("Injected failure after the date update.");
            return result;
        }
    }
}
