using FluentAssertions;
using Moq;
using NGB.Attachments;
using NGB.Core.AuditLog;
using Xunit;

namespace NGB.Runtime.Tests.AttachmentsNotes;

public sealed class AttachmentUploadExpirationServiceTests
{
    [Fact]
    public async Task Expired_uploads_are_audited_once_on_parent_and_release_reserved_capacity()
    {
        var f = new ContentFixture();
        f.Limits.MaxActivePerObject = 1;
        var upload = await f.Upload();
        (await f.Expiration.ExpirePendingAsync(default)).Should().Be(0);
        await Assert.ThrowsAsync<AttachmentException>(() => f.Upload());
        f.Time.Now += TimeSpan.FromDays(2);
        f.Storage.Invocations.Clear();

        (await f.Expiration.ExpirePendingAsync(default)).Should().Be(1);
        (await f.Expiration.ExpirePendingAsync(default)).Should().Be(0);

        var expired = f.AttachmentRows[upload.AttachmentId];
        expired.Status.Should().Be(AttachmentStatus.Deleted);
        expired.DeletedAtUtc.Should().Be(f.Time.Now.UtcDateTime);
        expired.StorageDeletedAtUtc.Should().BeNull();
        f.Storage.VerifyNoOtherCalls();
        f.Audit.Verify(x => x.WriteAsync(AuditEntityKind.Document, f.Target.Id, "attachments.upload_expired",
            It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), null, default), Times.Once);
        await f.Upload();
    }

    [Fact]
    public async Task Expiration_uses_bounded_batches_and_preserves_ready_and_recent_uploads()
    {
        var f = new ContentFixture();
        f.Limits.ExpirationBatchSize = 1;
        var ready = await f.Upload();
        await f.AttachmentService.CompleteAsync(ready.AttachmentId, default);
        var first = await f.Upload();
        var second = await f.Upload();
        f.Time.Now += TimeSpan.FromDays(2);
        var recent = await f.Upload();
        f.Storage.Invocations.Clear();

        (await f.Expiration.ExpirePendingAsync(default)).Should().Be(1);
        f.AttachmentRows.Values.Count(x => x.Status == AttachmentStatus.Deleted).Should().Be(1);
        (await f.Expiration.ExpirePendingAsync(default)).Should().Be(1);
        (await f.Expiration.ExpirePendingAsync(default)).Should().Be(0);

        f.AttachmentRows[first.AttachmentId].Status.Should().Be(AttachmentStatus.Deleted);
        f.AttachmentRows[second.AttachmentId].Status.Should().Be(AttachmentStatus.Deleted);
        f.AttachmentRows[ready.AttachmentId].Status.Should().Be(AttachmentStatus.Ready);
        f.AttachmentRows[recent.AttachmentId].Status.Should().Be(AttachmentStatus.PendingUpload);
        f.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancellation_rolls_back_the_transaction_and_reaches_the_caller()
    {
        var f = new ContentFixture();
        using var cancellation = new CancellationTokenSource();
        f.Attachments.Setup(x => x.LockStalePendingAsync(It.IsAny<DateTime>(), It.IsAny<int>(), cancellation.Token))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Expiration.ExpirePendingAsync(cancellation.Token));

        f.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        f.Uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        f.Audit.VerifyNoOtherCalls();
        f.Storage.VerifyNoOtherCalls();
    }
}
