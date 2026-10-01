using FluentAssertions;
using Moq;
using NGB.Attachments;
using NGB.Persistence.Outbox;
using Xunit;

namespace NGB.Runtime.Tests.AttachmentsNotes;

public sealed class AttachmentMaintenanceTests
{
    [Fact]
    public async Task Stale_cleanup_marks_pending_terminal_then_deletes_both_objects_idempotently()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        (await f.Maintenance.ExpirePendingAsync(default)).Should().Be(0);
        f.Time.Now += TimeSpan.FromDays(2);
        (await f.Maintenance.ExpirePendingAsync(default)).Should().Be(1);
        (await f.Maintenance.ExpirePendingAsync(default)).Should().Be(0);
        f.AttachmentRows[upload.AttachmentId].Status.Should().Be(AttachmentStatus.Deleted);
        await f.Maintenance.ProcessCleanupAsync(default);
        await f.Maintenance.ProcessCleanupAsync(default);
        f.AttachmentRows[upload.AttachmentId].StorageDeletedAtUtc.Should().Be(f.Time.Now.UtcDateTime);
        f.Storage.Verify(x => x.DeleteObjectAsync($"uploads/{upload.AttachmentId:N}", default), Times.Exactly(2));
        f.Storage.Verify(x => x.DeleteObjectAsync($"attachments/{upload.AttachmentId:N}", default), Times.Exactly(2));
    }

    [Fact]
    public async Task Staging_cleanup_waits_for_signed_put_expiry_and_never_deletes_ready_bytes()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await f.AttachmentService.CompleteAsync(upload.AttachmentId, default);
        await f.Maintenance.ProcessCleanupAsync(default);
        f.Storage.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), default), Times.Never);
        f.Outbox.Verify(x => x.MarkFailedAsync(It.IsAny<Guid>(), "attachments-cleanup", 1, It.IsAny<DateTime>(),
            f.Time.Now.UtcDateTime.AddMinutes(15), It.IsAny<string>(), false, default), Times.Once);
        f.Time.Now += TimeSpan.FromMinutes(16);
        await f.Maintenance.ProcessCleanupAsync(default);
        f.Storage.Verify(x => x.DeleteObjectAsync($"uploads/{upload.AttachmentId:N}", default), Times.Once);
        f.Storage.Verify(x => x.DeleteObjectAsync($"attachments/{upload.AttachmentId:N}", default), Times.Never);
        f.AttachmentRows[upload.AttachmentId].StorageDeletedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(8, true)]
    public async Task Failures_retry_with_bounded_backoff_and_dead_letter(int attempt, bool deadLetter)
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);
        f.Time.Now += TimeSpan.FromDays(2);
        f.Outbox.Setup(x => x.ClaimBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), default))
            .ReturnsAsync([new OutboxConsumerWorkItem(f.Events[0], "attachments-cleanup", attempt)]);
        f.Storage.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new AttachmentStorageUnavailableException());
        await f.Maintenance.ProcessCleanupAsync(default);
        f.Outbox.Verify(x => x.MarkFailedAsync(It.IsAny<Guid>(), "attachments-cleanup", attempt, It.IsAny<DateTime>(),
            It.Is<DateTime?>(date => deadLetter ? date == null : date > f.Time.Now.UtcDateTime),
            "AttachmentStorageUnavailableException", deadLetter, default), Times.Once);
        f.AttachmentRows[upload.AttachmentId].StorageDeletedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("unexpected", 1, "{}")]
    [InlineData("ngb.attachment.cleanup.v1", 2, "{}")]
    [InlineData("ngb.attachment.cleanup.v1", 1, "null")]
    public async Task Invalid_events_are_recorded_as_failed(string type, int version, string payload)
    {
        var f = new ContentFixture();
        var id = Guid.CreateVersion7();
        f.Events.Add(new(id, type, version, f.Time.Now.UtcDateTime, "test", "test", null, id, null, payload,
            f.Time.Now.UtcDateTime));
        await f.Maintenance.ProcessCleanupAsync(default);
        f.Outbox.Verify(
            x => x.MarkFailedAsync(id, "attachments-cleanup", 1, It.IsAny<DateTime>(), It.IsAny<DateTime?>(),
                "JsonException", false, default), Times.Once);
    }

    [Fact]
    public async Task Missing_metadata_is_idempotent_and_live_metadata_cannot_be_deleted()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);
        f.Time.Now += TimeSpan.FromDays(2);
        var row = f.AttachmentRows[upload.AttachmentId];
        f.AttachmentRows[upload.AttachmentId] = row with { Status = AttachmentStatus.Ready };
        await f.Maintenance.ProcessCleanupAsync(default);
        f.Storage.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), default), Times.Never);
        f.AttachmentRows.Clear();
        await f.Maintenance.ProcessCleanupAsync(default);
        f.Outbox.Verify(
            x => x.MarkCompletedAsync(It.IsAny<Guid>(), "attachments-cleanup", 1, It.IsAny<DateTime>(), default),
            Times.Once);
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_consuming_a_retry()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);
        f.Time.Now += TimeSpan.FromDays(2);
        using var cts = new CancellationTokenSource();
        f.Storage.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), cts.Token)).Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Maintenance.ProcessCleanupAsync(cts.Token));
        f.Outbox.Verify(
            x => x.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
