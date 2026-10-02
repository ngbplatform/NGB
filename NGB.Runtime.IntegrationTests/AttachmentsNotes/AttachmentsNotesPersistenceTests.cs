using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Notes;
using NGB.Persistence.Attachments;
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

    private IHost Host(int maxAttachments = 100) => IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
    {
        services.AddNgbFeatureManagement(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Attachments"] = bool.TrueString,
            ["FeatureManagement:Notes"] = bool.TrueString
        }).Build());
        services.AddNgbAttachmentsAndNotes(options => options.MaxActivePerObject = maxAttachments);
        services.AddSingleton<IAttachmentObjectStorage, VerifiedTestStorage>();
        services.AddScoped<IBusinessObjectResolver, TestResolver>();
        services.AddScoped<INgbAccessChecker, TestAccess>();
        services.AddScoped<ICurrentActorContext, TestActor>();
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

        public Task DeleteObjectAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }
}
