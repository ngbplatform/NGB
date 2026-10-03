using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Features;
using NGB.Attachments;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Core.AuditLog;
using NGB.Core.Features;
using NGB.Runtime.Attachments;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.AttachmentsNotes;

public sealed class ContentAuditTests
{
    [Theory]
    [InlineData("parent")]
    [InlineData("content")]
    [InlineData("audit")]
    public async Task Deleted_attachment_is_downloadable_only_with_parent_content_and_audit_access(string deniedAccess)
    {
        var f = new ContentFixture();
        var features = EnabledFeatures();
        var access = new ContentAuditAccess(features.Object, f.Access.Object, f.ContentAccess);
        var service = new AttachmentAuditService(features.Object, f.Attachments.Object, access,
            () => f.Storage.Object, Options.Create(f.Limits), f.Time);
        var upload = await f.Upload();
        await f.AttachmentService.CompleteAsync(upload.AttachmentId, default);
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);

        (await service.DownloadAsync(upload.AttachmentId, default)).Url.Should().Be("https://storage/download");
        f.Access.Verify(x => x.RequireAsync("document", "invoice", "view_audit", default), Times.Once);
        f.Access.Verify(x => x.RequireAsync("system", "attachments", "read", default), Times.Once);
        f.Storage.Invocations.Clear();
        var denied = new NgbPermissionDeniedException(new("document", "invoice", "view_audit"));
        switch (deniedAccess)
        {
            case "parent":
                f.Resolver.Setup(x => x.ResolveAsync(f.Target, default)).ThrowsAsync(denied);
                break;
            case "content":
                f.Access.Setup(x => x.RequireAsync("system", "attachments", "read", default)).ThrowsAsync(denied);
                break;
            case "audit":
                f.Access.Setup(x => x.RequireAsync("document", "invoice", "view_audit", default)).ThrowsAsync(denied);
                break;
        }

        await Assert.ThrowsAsync<NgbPermissionDeniedException>(() => service.DownloadAsync(upload.AttachmentId, default));
        f.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Disabled_feature_or_missing_bytes_cannot_issue_a_download_url()
    {
        var f = new ContentFixture();
        var features = EnabledFeatures();
        var access = new ContentAuditAccess(features.Object, f.Access.Object, f.ContentAccess);
        var service = new AttachmentAuditService(features.Object, f.Attachments.Object, access,
            () => throw new InvalidOperationException("Storage must not be resolved"), Options.Create(f.Limits), f.Time);
        var upload = await f.Upload();

        (await Assert.ThrowsAsync<AttachmentException>(() => service.DownloadAsync(upload.AttachmentId, default)))
            .ErrorCode.Should().Be("attachments.not_available");
        (await Assert.ThrowsAsync<AttachmentException>(() => service.DownloadAsync(Guid.CreateVersion7(), default)))
            .ErrorCode.Should().Be("attachments.not_found");
        features.Setup(x => x.RequireAsync(NgbFeatures.Attachments, default))
            .ThrowsAsync(new InvalidOperationException("Disabled"));
        f.Attachments.Invocations.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(upload.AttachmentId, default));
        f.Attachments.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(BusinessObjectKind.CatalogItem, AuditEntityKind.Catalog)]
    [InlineData(BusinessObjectKind.Document, AuditEntityKind.Document)]
    [InlineData(BusinessObjectKind.GeneralJournalEntry, AuditEntityKind.Document)]
    public async Task Parent_audit_hides_content_without_capability_and_preserves_pagination(
        BusinessObjectKind kind, AuditEntityKind auditKind)
    {
        var f = new ContentFixture();
        var features = EnabledFeatures();
        var target = f.Target with { Kind = kind };
        var metadata = JsonSerializer.Serialize(new { target }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var note = new AuditEventDto(Guid.CreateVersion7(), (short)auditKind, target.Id, "notes.updated",
            null, f.Time.Now.UtcDateTime, null, metadata, [new("note.text", "\"before\"", "\"secret\"")]);
        var parent = note with { AuditEventId = Guid.CreateVersion7(), ActionCode = "document.update", Changes = [] };
        var cursor = new AuditCursorDto(note.OccurredAtUtc, note.AuditEventId);
        var page = new AuditLogPageDto([note, parent], cursor, 25);
        var filter = new ContentAuditAccess(features.Object, f.Access.Object, f.ContentAccess);

        var hidden = await filter.FilterAsync(page, default);
        hidden.Items.Should().Equal(parent);
        hidden.NextCursor.Should().Be(cursor);
        f.Access.Setup(x => x.HasAsync("system", "notes", "read", default)).ReturnsAsync(true);
        (await filter.FilterAsync(page, default)).Items.Should().Equal(note, parent);
        f.Resolver.Verify(x => x.ResolveAsync(target, default), Times.Once);

        features.Setup(x => x.IsEnabledAsync(NgbFeatures.Notes, default)).ReturnsAsync(false);
        (await filter.FilterAsync(page, default)).Items.Should().Equal(parent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"target\":{\"kind\":2,\"typeCode\":\"invoice\",\"id\":\"00000000-0000-0000-0000-000000000000\"}}")]
    public async Task Content_audit_without_a_matching_parent_never_exposes_payload(string? metadata)
    {
        var f = new ContentFixture();
        f.Access.Setup(x => x.HasAsync("system", "notes", "read", default)).ReturnsAsync(true);
        var filter = new ContentAuditAccess(EnabledFeatures().Object, f.Access.Object, f.ContentAccess);
        var item = new AuditEventDto(Guid.CreateVersion7(), 1, f.Target.Id, "notes.created", null,
            f.Time.Now.UtcDateTime, null, metadata, [new("note.text", null, "\"secret\"")]);

        (await filter.FilterAsync(new([item], null, 25), default)).Items.Should().BeEmpty();
        f.Resolver.VerifyNoOtherCalls();
    }

    private static Mock<INgbFeatureService> EnabledFeatures()
    {
        var features = new Mock<INgbFeatureService>();
        features.Setup(x => x.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return features;
    }
}
