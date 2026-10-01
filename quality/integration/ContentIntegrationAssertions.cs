using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Services;
using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Notes;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.IntegrationTests.Content;

internal static class ContentIntegrationAssertions
{
    public static void Configure(IServiceCollection services)
    {
        services.AddNgbAttachmentsAndNotes();
        services.AddSingleton<IAttachmentObjectStorage, VerifiedStorage>();
        services.AddSingleton<ContentAccess>();
        services.AddSingleton<INgbAccessChecker>(sp => sp.GetRequiredService<ContentAccess>());
        services.AddSingleton<ICurrentActorContext, ContentActor>();
    }

    public static async Task VerifyAsync(IServiceProvider services, BusinessObjectKind kind, string typeCode, Guid id)
    {
        var target = new BusinessObjectRef(kind, typeCode, id);
        var attachments = services.GetRequiredService<IAttachmentService>();
        var notes = services.GetRequiredService<INoteService>();
        var summaries = services.GetRequiredService<IBusinessObjectContentSummaryService>();
        var access = services.GetRequiredService<ContentAccess>();

        async Task<string> Parent() => kind == BusinessObjectKind.CatalogItem
            ? JsonSerializer.Serialize(await services.GetRequiredService<ICatalogService>()
                .GetByIdAsync(typeCode, id, default))
            : JsonSerializer.Serialize(await services.GetRequiredService<IDocumentService>()
                .GetByIdAsync(typeCode, id, default));

        var parent = await Parent();
        var before = await summaries.GetAsync(target, default);
        var upload = await attachments.CreateUploadAsync(new(target, "evidence.txt", "text/plain", 3), default);
        await attachments.CompleteAsync(upload.AttachmentId, default);
        var note = await notes.CreateAsync(new(target, "Independent platform note"), default);
        var edited = await notes.UpdateAsync(note.Id, new("Updated platform note", note.Version), default);
        edited.Version.Should().Be(2);
        (await summaries.GetAsync(target, default)).Should()
            .Be(new BusinessObjectContentSummary(before.Attachments + 1, before.Notes + 1));
        (await attachments.ListAsync(target, 100, null, default)).Items.Should()
            .Contain(x => x.Id == upload.AttachmentId);
        (await notes.ListAsync(target, 100, null, default)).Items.Should()
            .Contain(x => x.Id == note.Id && x.Text == edited.Text);
        (await attachments.DownloadAsync(upload.AttachmentId, default)).Url.Should().StartWith("https://storage.test/");
        (await Parent()).Should().Be(parent, "content must not mutate the parent, version, or workflow");
        access.Denied = true;

        try
        {
            await Assert.ThrowsAsync<NgbPermissionDeniedException>(() =>
                attachments.ListAsync(target, 50, null, default));
            await Assert.ThrowsAsync<NgbPermissionDeniedException>(() =>
                notes.CreateAsync(new(target, "denied"), default));
            await Assert.ThrowsAsync<NgbPermissionDeniedException>(() =>
                attachments.DownloadAsync(upload.AttachmentId, default));
        }
        finally
        {
            access.Denied = false;
        }

        (await summaries.GetAsync(target, default)).Should()
            .Be(new BusinessObjectContentSummary(before.Attachments + 1, before.Notes + 1));
    }

    private sealed class ContentActor : ICurrentActorContext
    {
        public ActorIdentity Current => new("content-vertical-tester", "content@example.test", "Content tester");
    }

    private sealed class ContentAccess : INgbAccessChecker
    {
        public bool Denied { get; set; }

        public Task<PermissionSnapshot> GetSnapshotAsync(CancellationToken ct) =>
            Task.FromResult(new PermissionSnapshot(null, "test", true, true, true, 1, []));

        public Task<bool> HasAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct) =>
            Task.FromResult(!Denied);

        public Task RequireAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct)
            => Denied
                ? Task.FromException(new NgbPermissionDeniedException(new(resourceKind, resourceCode, actionCode)))
                : Task.CompletedTask;
    }

    private sealed class VerifiedStorage : IAttachmentObjectStorage
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
