using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Services;
using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Audit;
using NGB.Core.AuditLog;
using NGB.Notes;
using NGB.Persistence.Attachments;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Notes;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AttachmentsNotes;

[Collection(AccountingPostgresCollection.Name)]
public sealed class AttachmentsNotesPersistenceTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(BusinessObjectKind.CatalogItem)]
    [InlineData(BusinessObjectKind.Document)]
    [InlineData(BusinessObjectKind.GeneralJournalEntry)]
    public async Task Resources_persist_page_count_and_soft_delete_independently(BusinessObjectKind kind)
    {
        using var host = Host();
        var target = new BusinessObjectRef(kind,
            kind == BusinessObjectKind.GeneralJournalEntry ? "general_journal_entry" : "test", Guid.CreateVersion7());
        Guid attachmentId;
        Guid noteId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
            var notes = scope.ServiceProvider.GetRequiredService<INoteService>();
            var summary = scope.ServiceProvider.GetRequiredService<IBusinessObjectContentSummaryService>();
            var upload = await attachments.CreateUploadAsync(new(target, "example.txt", "text/plain", 3), default);
            attachmentId = upload.AttachmentId;
            (await summary.GetAsync(target, default)).Attachments.Should().Be(0);
            await attachments.CompleteAsync(attachmentId, default);
            var note = await notes.CreateAsync(new(target, "Original"), default);
            noteId = note.Id;
            await notes.UpdateAsync(noteId, new("Edited", note.Version), default);
            for (var i = 0; i < 3; i++) await notes.CreateAsync(new(target, $"Page {i}"), default);
            (await summary.GetAsync(target, default)).Should().Be(new BusinessObjectContentSummary(1, 4));
            var page = await notes.ListAsync(target, 2, null, default);
            var tail = await notes.ListAsync(target, 2, page.NextCursor, default);
            page.Items.Should().HaveCount(2);
            tail.Items.Should().HaveCount(2);
            tail.NextCursor.Should().BeNull();
            page.Items.Select(x => x.Id).Intersect(tail.Items.Select(x => x.Id)).Should().BeEmpty();
            page.Items.Concat(tail.Items).Single(x => x.Id == noteId).Text.Should().Be("Edited");
            (await attachments.ListAsync(target, 1, null, default)).Items.Single().CreatedByDisplayName.Should()
                .Be("Content tester");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAttachmentService>().DeleteAsync(attachmentId, default);
            await scope.ServiceProvider.GetRequiredService<INoteService>().DeleteAsync(noteId, 2, default);
            var attachment = await scope.ServiceProvider.GetRequiredService<IAttachmentRepository>()
                .GetAsync(attachmentId, false, default);
            attachment!.Status.Should().Be(AttachmentStatus.Deleted);
            attachment.StorageDeletedAtUtc.Should().BeNull();
            (await scope.ServiceProvider.GetRequiredService<INoteRepository>().GetAsync(noteId, false, default))!
                .IsDeleted.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<IBusinessObjectContentSummaryService>()
                .GetAsync(target, default)).Should().Be(new BusinessObjectContentSummary(0, 3));
        }
    }

    [Fact]
    public async Task Concurrent_uploads_cannot_bypass_reserved_capacity()
    {
        using var host = Host(maxAttachments: 1);
        var target = new BusinessObjectRef(BusinessObjectKind.Document, "test", Guid.CreateVersion7());

        async Task<bool> Upload()
        {
            await using var scope = host.Services.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IAttachmentService>()
                    .CreateUploadAsync(new(target, "file.txt", "text/plain", 3), default);
                return true;
            }
            catch (AttachmentException ex) when (ex.ErrorCode == "attachments.count_limit_exceeded")
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Upload()));
        results.Count(x => x).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_note_edits_have_one_winner_and_one_explicit_conflict()
    {
        using var host = Host();
        var target = new BusinessObjectRef(BusinessObjectKind.Document, "test", Guid.CreateVersion7());
        Guid noteId;
        await using (var scope = host.Services.CreateAsyncScope())
            noteId = (await scope.ServiceProvider.GetRequiredService<INoteService>()
                .CreateAsync(new(target, "original"), default)).Id;

        async Task<bool> Edit(string text)
        {
            await using var scope = host.Services.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<INoteService>()
                    .UpdateAsync(noteId, new(text, 1), default);
                return true;
            }
            catch (NoteException ex) when (ex.ErrorCode == "notes.version_conflict")
            {
                return false;
            }
        }

        (await Task.WhenAll(Edit("first"), Edit("second"))).Count(x => x).Should().Be(1);
        await using var verifiedScope = host.Services.CreateAsyncScope();
        var audit = await verifiedScope.ServiceProvider.GetRequiredService<IAuditLogQueryService>()
            .GetEntityAuditLogAsync(AuditEntityKind.Document, target.Id, null, null, 25, default);
        audit.Items.Count(x => x.ActionCode == "notes.updated").Should().Be(1);
        var stored = await verifiedScope.ServiceProvider.GetRequiredService<INoteRepository>().GetAsync(noteId, false, default);
        var change = audit.Items.Single(x => x.ActionCode == "notes.updated").Changes.Single(x => x.FieldPath == "note.text");
        JsonSerializer.Deserialize<string>(change.NewValueJson!).Should().Be(stored!.Text);
    }

    [Fact]
    public async Task Document_header_and_version_do_not_change_after_content_writes()
    {
        using var host = Host();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var id = await services.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync("test", "D-1", DateTime.UtcNow);
        var repository = services.GetRequiredService<IDocumentRepository>();
        var before = await repository.GetAsync(id);
        var target = new BusinessObjectRef(BusinessObjectKind.Document, "test", id);
        var upload = await services.GetRequiredService<IAttachmentService>()
            .CreateUploadAsync(new(target, "test.txt", "text/plain", 3), default);
        await services.GetRequiredService<IAttachmentService>().CompleteAsync(upload.AttachmentId, default);
        await services.GetRequiredService<INoteService>().CreateAsync(new(target, "independent"), default);
        (await repository.GetAsync(id)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Missing_rows_seek_pages_and_stale_reservations_use_bounded_provider_queries()
    {
        using var host = Host();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var attachments = services.GetRequiredService<IAttachmentService>();
        var repository = services.GetRequiredService<IAttachmentRepository>();
        var notes = services.GetRequiredService<INoteRepository>();
        var target = new BusinessObjectRef(BusinessObjectKind.CatalogItem, "test", Guid.CreateVersion7());
        (await repository.GetAsync(Guid.CreateVersion7(), false, default)).Should().BeNull();
        (await notes.GetAsync(Guid.CreateVersion7(), false, default)).Should().BeNull();
        for (var i = 0; i < 3; i++)
        {
            var upload = await attachments.CreateUploadAsync(new(target, "page.txt", "text/plain", 3), default);
            await attachments.CompleteAsync(upload.AttachmentId, default);
        }

        var page = await attachments.ListAsync(target, 2, null, default);
        (await attachments.ListAsync(target, 2, page.NextCursor, default)).Items.Should().ContainSingle();
        var pending = await attachments.CreateUploadAsync(new(target, "pending.txt", "text/plain", 3), default);
        var uow = services.GetRequiredService<NGB.Persistence.UnitOfWork.IUnitOfWork>();
        await uow.BeginTransactionAsync(default);
        var stale = await repository.LockStalePendingAsync(DateTime.UtcNow.AddDays(1), 1, default);
        stale.Should().ContainSingle().Which.Id.Should().Be(pending.AttachmentId);
        await uow.RollbackAsync(default);
        var summary = services.GetRequiredService<IBusinessObjectContentSummaryReader>();
        (await summary.GetAsync(target, false, false, default)).Should()
            .Be(new BusinessObjectContentSummary(null, null));
    }

    [Theory]
    [InlineData(BusinessObjectKind.CatalogItem, AuditEntityKind.Catalog)]
    [InlineData(BusinessObjectKind.Document, AuditEntityKind.Document)]
    [InlineData(BusinessObjectKind.GeneralJournalEntry, AuditEntityKind.Document)]
    public async Task Parent_audit_retains_note_changes_and_deleted_attachment_with_actor_and_seek_pages(
        BusinessObjectKind kind, AuditEntityKind auditKind)
    {
        using var host = Host();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var target = new BusinessObjectRef(kind,
            kind == BusinessObjectKind.GeneralJournalEntry ? "general_journal_entry" : "test", Guid.CreateVersion7());
        var attachments = services.GetRequiredService<IAttachmentService>();
        var notes = services.GetRequiredService<INoteService>();
        var audit = services.GetRequiredService<IAuditLogQueryService>();
        var upload = await attachments.CreateUploadAsync(new(target, "my_file.txt", "text/plain", 3), default);
        await attachments.CompleteAsync(upload.AttachmentId, default);
        var note = await notes.CreateAsync(new(target, "original_text\n<script>plain</script>"), default);
        await notes.UpdateAsync(note.Id, new("2026-10-02", note.Version), default);
        await notes.DeleteAsync(note.Id, 2, default);
        await notes.DeleteAsync(note.Id, 2, default);
        await attachments.DeleteAsync(upload.AttachmentId, default);
        await attachments.DeleteAsync(upload.AttachmentId, default);

        var events = new List<AuditEventDto>();
        AuditCursorDto? cursor = null;
        do
        {
            var page = await audit.GetEntityAuditLogAsync(auditKind, target.Id,
                cursor?.OccurredAtUtc, cursor?.AuditEventId, 2, default);
            events.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        events.Should().HaveCount(6).And.OnlyHaveUniqueItems(x => x.AuditEventId);
        events.Should().OnlyContain(x => x.EntityId == target.Id && x.EntityKind == (short)auditKind
            && x.Actor != null && x.Actor.DisplayName == "Content tester" && x.Actor.UserId != null);
        var edit = events.Single(x => x.ActionCode == "notes.updated");
        var change = edit.Changes.Single(x => x.FieldPath == "note.text");
        JsonSerializer.Deserialize<string>(change.OldValueJson!).Should().Be(note.Text);
        JsonSerializer.Deserialize<string>(change.NewValueJson!).Should().Be("2026-10-02");
        var deleted = events.Single(x => x.ActionCode == "notes.mark_for_deletion");
        JsonSerializer.Deserialize<string>(deleted.Changes.Single(x => x.FieldPath == "note.text").NewValueJson!)
            .Should().Be("2026-10-02");
        var attachment = events.Single(x => x.ActionCode == "attachments.mark_for_deletion");
        using var metadata = JsonDocument.Parse(attachment.MetadataJson!);
        metadata.RootElement.GetProperty("attachmentId").GetGuid().Should().Be(upload.AttachmentId);
        metadata.RootElement.GetProperty("downloadAvailable").GetBoolean().Should().BeTrue();
        (await attachments.ListAsync(target, 50, null, default)).Items.Should().BeEmpty();
        (await notes.ListAsync(target, 50, null, default)).Items.Should().BeEmpty();
        (await services.GetRequiredService<IAttachmentAuditService>().DownloadAsync(upload.AttachmentId, default))
            .Url.Should().Be("https://storage.test/download");
    }

    [Fact]
    public async Task Failed_audit_write_rolls_back_content_change_in_the_same_database_transaction()
    {
        var target = new BusinessObjectRef(BusinessObjectKind.Document, "test", Guid.CreateVersion7());
        Guid noteId;
        Guid attachmentId;
        using (var host = Host())
        {
            await using var scope = host.Services.CreateAsyncScope();
            noteId = (await scope.ServiceProvider.GetRequiredService<INoteService>()
                .CreateAsync(new(target, "original"), default)).Id;
            attachmentId = (await scope.ServiceProvider.GetRequiredService<IAttachmentService>()
                .CreateUploadAsync(new(target, "retained.txt", "text/plain", 3), default)).AttachmentId;
        }

        using (var host = Host(failAudit: true))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<INoteService>()
                .UpdateAsync(noteId, new("must roll back", 1), default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAttachmentService>()
                .DeleteAsync(attachmentId, default));
        }

        using var verifiedHost = Host();
        await using var verifiedScope = verifiedHost.Services.CreateAsyncScope();
        var services = verifiedScope.ServiceProvider;
        var note = await services.GetRequiredService<INoteRepository>().GetAsync(noteId, false, default);
        note!.Text.Should().Be("original");
        note.Version.Should().Be(1);
        (await services.GetRequiredService<IAttachmentRepository>().GetAsync(attachmentId, false, default))!
            .Status.Should().Be(AttachmentStatus.PendingUpload);
        var audit = await services.GetRequiredService<IAuditLogQueryService>()
            .GetEntityAuditLogAsync(AuditEntityKind.Document, target.Id, null, null, 25, default);
        audit.Items.Select(x => x.ActionCode).Should().BeEquivalentTo("notes.created", "attachments.upload_requested");
    }

    [Fact]
    public async Task Expired_uploads_release_capacity_and_keep_audit_atomic_in_PostgreSql()
    {
        var clock = new UploadExpirationClock();
        var target = new BusinessObjectRef(BusinessObjectKind.Document, "test", Guid.CreateVersion7());
        Guid pendingId;
        Guid readyId;
        using (var host = Host(maxAttachments: 2, clock: clock))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
            readyId = (await attachments.CreateUploadAsync(new(target, "ready.txt", "text/plain", 3), default)).AttachmentId;
            await attachments.CompleteAsync(readyId, default);
            pendingId = (await attachments.CreateUploadAsync(new(target, "pending.txt", "text/plain", 3), default)).AttachmentId;
            await Assert.ThrowsAsync<AttachmentException>(() =>
                attachments.CreateUploadAsync(new(target, "blocked.txt", "text/plain", 3), default));
        }

        clock.Now += TimeSpan.FromDays(2);
        using (var host = Host(failAudit: true, clock: clock))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<IAttachmentUploadExpirationService>().ExpirePendingAsync(default));
        }

        using var verifiedHost = Host(maxAttachments: 2, clock: clock);
        await using var verifiedScope = verifiedHost.Services.CreateAsyncScope();
        var services = verifiedScope.ServiceProvider;
        var repository = services.GetRequiredService<IAttachmentRepository>();
        (await repository.GetAsync(pendingId, false, default))!.Status.Should().Be(AttachmentStatus.PendingUpload);
        var expiration = services.GetRequiredService<IAttachmentUploadExpirationService>();

        (await expiration.ExpirePendingAsync(default)).Should().Be(1);
        (await expiration.ExpirePendingAsync(default)).Should().Be(0);

        var expired = (await repository.GetAsync(pendingId, false, default))!;
        expired.Status.Should().Be(AttachmentStatus.Deleted);
        expired.DeletedAtUtc.Should().Be(clock.Now.UtcDateTime);
        expired.StorageDeletedAtUtc.Should().BeNull();
        (await repository.GetAsync(readyId, false, default))!.Status.Should().Be(AttachmentStatus.Ready);
        var audit = await services.GetRequiredService<IAuditLogQueryService>()
            .GetEntityAuditLogAsync(AuditEntityKind.Document, target.Id, null, null, 25, default);
        audit.Items.Count(x => x.ActionCode == "attachments.upload_expired").Should().Be(1);
        await services.GetRequiredService<IAttachmentService>()
            .CreateUploadAsync(new(target, "available.txt", "text/plain", 3), default);
    }

    private sealed class UploadExpirationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FailingAuditWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("Audit write failed");

        public Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
            => throw new InvalidOperationException("Audit write failed");
    }

    private IHost Host(int maxAttachments = 100, bool failAudit = false, TimeProvider? clock = null) => IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
    {
        services.AddNgbFeatureManagement(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Attachments"] = bool.TrueString,
            ["FeatureManagement:Notes"] = bool.TrueString
        }).Build());
        services.AddNgbRuntimeAuthorization();
        services.AddNgbAttachmentsAndNotes(options => options.MaxActivePerObject = maxAttachments);
        services.AddSingleton<IAttachmentObjectStorage, VerifiedTestStorage>();
        services.AddScoped<IBusinessObjectResolver, TestResolver>();
        services.AddScoped<INgbAccessChecker, TestAccess>();
        services.AddScoped<ICurrentActorContext, TestActor>();
        if (failAudit)
            services.AddScoped<IAuditEventWriter, FailingAuditWriter>();
        if (clock is not null)
            services.AddSingleton(clock);
    });

    private sealed class TestResolver : IBusinessObjectResolver
    {
        public Task<ResolvedBusinessObject> ResolveAsync(BusinessObjectRef target, CancellationToken ct) =>
            Task.FromResult(new ResolvedBusinessObject(target, "Test"));
    }

    private sealed class TestActor : ICurrentActorContext
    {
        public ActorIdentity Current => new("content-integration-user", "content@example.test", "Content tester");
    }

    private sealed class TestAccess : INgbAccessChecker
    {
        public Task<PermissionSnapshot> GetSnapshotAsync(CancellationToken ct) =>
            Task.FromResult(new PermissionSnapshot(null, "test", true, true, true, 1, []));

        public Task<bool> HasAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct) =>
            Task.FromResult(true);

        public Task RequireAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class VerifiedTestStorage : IAttachmentObjectStorage
    {
        public Task<AttachmentUploadTarget> CreateUploadTargetAsync(string key, string contentType, TimeSpan lifetime,
            CancellationToken ct) =>
            Task.FromResult(new AttachmentUploadTarget("https://storage.test/upload",
                new Dictionary<string, string>()));

        public Task<AttachmentStoredObject?> GetObjectInfoAsync(string key, CancellationToken ct) =>
            Task.FromResult<AttachmentStoredObject?>(new(3, "text/plain", "etag"));

        public Task SealUploadAsync(string uploadKey, string destinationKey, string expectedETag,
            CancellationToken ct) => Task.CompletedTask;

        public Task<string> CreateDownloadTargetAsync(string key, string fileName, TimeSpan lifetime,
            CancellationToken ct) => Task.FromResult("https://storage.test/download");

        public Task DeleteObjectAsync(string key, CancellationToken ct)
            => throw new InvalidOperationException("Files must be retained.");
    }
}
