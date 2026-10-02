using FluentAssertions;
using Moq;
using NGB.Attachments;
using NGB.Core.AuditLog;
using NGB.Notes;
using NGB.Runtime.Attachments;
using NGB.Runtime.Security;
using NGB.Core.Security;
using Xunit;

namespace NGB.Runtime.Tests.AttachmentsNotes;

public sealed class AttachmentsNotesTests
{
    [Fact]
    public async Task Upload_verifies_storage_seals_separate_key_and_completion_is_idempotent()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        f.AttachmentRows[upload.AttachmentId].Status.Should().Be(AttachmentStatus.PendingUpload);
        (await f.AttachmentService.ListAsync(f.Target, 50, null, default)).Items.Should().BeEmpty();
        var completed = await f.AttachmentService.CompleteAsync(upload.AttachmentId, default);
        completed.SizeBytes.Should().Be(3);
        (await f.AttachmentService.CompleteAsync(upload.AttachmentId, default)).Should().BeEquivalentTo(completed);
        f.Storage.Verify(
            x => x.SealUploadAsync($"uploads/{upload.AttachmentId:N}", $"attachments/{upload.AttachmentId:N}", "etag",
                default), Times.Once);
        f.AttachmentRows[upload.AttachmentId].CompletedAtUtc.Should().Be(f.Time.Now.UtcDateTime);
        (await f.AttachmentService.DownloadAsync(upload.AttachmentId, default)).Url.Should()
            .Be("https://storage/download");
        f.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_is_logical_atomic_with_outbox_and_idempotent_during_storage_outage()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await f.AttachmentService.CompleteAsync(upload.AttachmentId, default);
        f.Storage.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new AttachmentStorageUnavailableException());
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);
        f.AttachmentRows[upload.AttachmentId].DeletedByUserId.Should().Be(f.ActorId);
        f.Events.Should().HaveCount(2);
        f.Storage.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), default), Times.Never);
        await AssertError(() => f.AttachmentService.DownloadAsync(upload.AttachmentId, default), "attachments.deleted");
        await AssertError(() => f.AttachmentService.CompleteAsync(upload.AttachmentId, default), "attachments.deleted");
    }

    [Fact]
    public async Task Complete_requires_existing_matching_bytes_and_unexpired_pending_state()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await AssertError(() => f.AttachmentService.DownloadAsync(upload.AttachmentId, default),
            "attachments.not_ready");
        f.Storage.Setup(x => x.GetObjectInfoAsync(It.IsAny<string>(), default))
            .ReturnsAsync((AttachmentStoredObject?)null);
        await AssertError(() => f.AttachmentService.CompleteAsync(upload.AttachmentId, default),
            "attachments.upload_missing");
        f.Storage.Setup(x => x.GetObjectInfoAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new AttachmentStoredObject(4, "text/plain", "etag"));
        await AssertError(() => f.AttachmentService.CompleteAsync(upload.AttachmentId, default),
            "attachments.size_mismatch");
        f.Storage.Setup(x => x.GetObjectInfoAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new AttachmentStoredObject(3, "text/html", "etag"));
        await AssertError(() => f.AttachmentService.CompleteAsync(upload.AttachmentId, default),
            "attachments.content_type_mismatch");
        f.Time.Now += TimeSpan.FromDays(2);
        await AssertError(() => f.AttachmentService.CompleteAsync(upload.AttachmentId, default),
            "attachments.upload_expired");
        f.AttachmentRows[upload.AttachmentId].Status.Should().Be(AttachmentStatus.PendingUpload);
        f.Uow.Verify(x => x.RollbackAsync(default), Times.AtLeast(4));
    }

    [Fact]
    public async Task Capacity_reserves_pending_uploads_and_releases_logically_deleted_slots()
    {
        var f = new ContentFixture();
        f.Limits.MaxActivePerObject = 1;
        var upload = await f.Upload();
        await AssertError(() => f.Upload(), "attachments.count_limit_exceeded");
        f.Attachments.Verify(x => x.LockTargetAsync(f.Target, default), Times.Exactly(2));
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);
        await f.Upload();
        await AssertError(
            () => f.AttachmentService.CreateUploadAsync(new(f.Target, "large", "text/plain", long.MaxValue), default),
            "attachments.file_too_large");
        await AssertError(
            () => f.AttachmentService.CreateUploadAsync(new(f.Target, "large", "text/plain", -1), default),
            "attachments.file_too_large");
    }

    [Fact]
    public async Task Every_attachment_operation_rechecks_parent_and_capability_access()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await f.AttachmentService.CompleteAsync(upload.AttachmentId, default);
        await f.AttachmentService.ListAsync(f.Target, 50, null, default);
        await f.AttachmentService.DownloadAsync(upload.AttachmentId, default);
        await f.AttachmentService.DeleteAsync(upload.AttachmentId, default);
        f.Resolver.Verify(x => x.ResolveAsync(f.Target, default), Times.Exactly(5));
        f.Access.Verify(x => x.RequireAsync("system", "attachments", "create", default), Times.Exactly(2));
        f.Access.Verify(x => x.RequireAsync("system", "attachments", "read", default), Times.Exactly(2));
        f.Access.Verify(x => x.RequireAsync("system", "attachments", "delete", default), Times.Once);
        f.Access.Setup(x => x.RequireAsync("system", "attachments", "read", default))
            .ThrowsAsync(new NgbPermissionDeniedException(new NgbPermissionKey("system", "attachments", "read")));
        await Assert.ThrowsAsync<NgbPermissionDeniedException>(() =>
            f.AttachmentService.DownloadAsync(upload.AttachmentId, default));
    }

    [Fact]
    public async Task Every_note_operation_rechecks_parent_and_capability_access()
    {
        var f = new ContentFixture();
        var note = await f.NoteService.CreateAsync(new(f.Target, "Initial note"), default);

        await f.NoteService.ListAsync(f.Target, 50, null, default);
        var updated = await f.NoteService.UpdateAsync(note.Id, new("Updated note", note.Version), default);
        await f.NoteService.DeleteAsync(note.Id, updated.Version, default);

        f.Resolver.Verify(x => x.ResolveAsync(f.Target, default), Times.Exactly(4));
        f.Access.Verify(x => x.RequireAsync("system", "notes", "create", default), Times.Once);
        f.Access.Verify(x => x.RequireAsync("system", "notes", "read", default), Times.Once);
        f.Access.Verify(x => x.RequireAsync("system", "notes", "update", default), Times.Once);
        f.Access.Verify(x => x.RequireAsync("system", "notes", "delete", default), Times.Once);
    }

    [Fact]
    public async Task Lists_use_bounded_seek_pages_and_hide_deleted_or_pending_resources()
    {
        var f = new ContentFixture();
        for (var i = 0; i < 3; i++)
        {
            var u = await f.Upload();
            await f.AttachmentService.CompleteAsync(u.AttachmentId, default);
        }

        var page = await f.AttachmentService.ListAsync(f.Target, 2, null, default);
        page.Items.Should().HaveCount(2);
        page.NextCursor.Should().NotBeNull();
        var tail = await f.AttachmentService.ListAsync(f.Target, 2, page.NextCursor, default);
        tail.Items.Should().ContainSingle();
        tail.NextCursor.Should().BeNull();
        await Assert.ThrowsAnyAsync<NGB.Tools.Exceptions.NgbException>(() =>
            f.AttachmentService.ListAsync(f.Target, 0, null, default));
        await Assert.ThrowsAnyAsync<NGB.Tools.Exceptions.NgbException>(() =>
            f.NoteService.ListAsync(f.Target, 101, null, default));
        await Assert.ThrowsAnyAsync<NGB.Tools.Exceptions.NgbException>(() =>
            f.NoteService.ListAsync(f.Target, 1, Guid.Empty, default));
    }

    [Fact]
    public async Task Notes_have_independent_concurrency_plain_text_and_logical_delete()
    {
        var f = new ContentFixture();
        var note = await f.NoteService.CreateAsync(new(f.Target, " <b>plain</b>\ntext "), default);
        note.Text.Should().Be("<b>plain</b>\ntext");
        note.Version.Should().Be(1);
        var edited = await f.NoteService.UpdateAsync(note.Id, new("edited", note.Version), default);
        edited.Version.Should().Be(2);
        edited.UpdatedByUserId.Should().Be(f.ActorId);
        await AssertNoteError(() => f.NoteService.UpdateAsync(note.Id, new("lost update", 1), default),
            "notes.version_conflict");
        await AssertNoteError(() => f.NoteService.DeleteAsync(note.Id, 1, default), "notes.version_conflict");
        await f.NoteService.DeleteAsync(note.Id, 2, default);
        await f.NoteService.DeleteAsync(note.Id, 2, default);
        (await f.NoteService.ListAsync(f.Target, 50, null, default)).Items.Should().BeEmpty();
        await AssertNoteError(() => f.NoteService.UpdateAsync(note.Id, new("restore?", 3), default), "notes.deleted");
        f.Audit.Verify(
            x => x.WriteAsync(AuditEntityKind.Note, note.Id, "notes.updated", null, It.IsAny<object>(), null, default),
            Times.Once);
    }

    [Fact]
    public async Task Note_pages_and_input_limits_are_enforced_and_every_write_is_authorized()
    {
        var f = new ContentFixture();
        for (var i = 0; i < 3; i++) await f.NoteService.CreateAsync(new(f.Target, $"note {i}"), default);
        var first = await f.NoteService.ListAsync(f.Target, 2, null, default);
        first.NextCursor.Should().NotBeNull();
        (await f.NoteService.ListAsync(f.Target, 2, first.NextCursor, default)).Items.Should().ContainSingle();
        f.Access.Verify(x => x.RequireAsync("system", "notes", "create", default), Times.Exactly(3));
        f.Resolver.Verify(x => x.ResolveAsync(f.Target, default), Times.Exactly(5));
        await AssertNoteError(() => f.NoteService.CreateAsync(new(f.Target, " "), default), "notes.invalid_text");
        await AssertNoteError(() => f.NoteService.CreateAsync(new(f.Target, "\0"), default), "notes.invalid_text");
        await AssertNoteError(() => f.NoteService.CreateAsync(new(f.Target, new string('x', 10001)), default),
            "notes.too_long");
        await AssertNoteError(() => f.NoteService.UpdateAsync(Guid.CreateVersion7(), new("text", 1), default),
            "notes.not_found");
        await AssertError(() => f.AttachmentService.CompleteAsync(Guid.CreateVersion7(), default),
            "attachments.not_found");
    }

    [Fact]
    public async Task Revoked_capabilities_and_parent_access_block_all_reads_and_mutations()
    {
        var f = new ContentFixture();
        var upload = await f.Upload();
        await f.AttachmentService.CompleteAsync(upload.AttachmentId, default);
        var note = await f.NoteService.CreateAsync(new(f.Target, "protected"), default);
        var calls = new Func<Task>[]
        {
            () => f.AttachmentService.ListAsync(f.Target, 50, null, default), () => f.Upload(),
            () => f.AttachmentService.CompleteAsync(upload.AttachmentId, default),
            () => f.AttachmentService.DownloadAsync(upload.AttachmentId, default),
            () => f.AttachmentService.DeleteAsync(upload.AttachmentId, default),
            () => f.NoteService.ListAsync(f.Target, 50, null, default),
            () => f.NoteService.CreateAsync(new(f.Target, "denied"), default),
            () => f.NoteService.UpdateAsync(note.Id, new("denied", 1), default),
            () => f.NoteService.DeleteAsync(note.Id, 1, default)
        };
        var denied = new NgbPermissionDeniedException(new NgbPermissionKey("system", "attachments", "read"));
        f.Access.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .ThrowsAsync(denied);
        foreach (var call in calls) await Assert.ThrowsAsync<NgbPermissionDeniedException>(call);
        f.Access.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);
        f.Resolver.Setup(x => x.ResolveAsync(f.Target, default)).ThrowsAsync(denied);
        foreach (var call in calls) await Assert.ThrowsAsync<NgbPermissionDeniedException>(call);
        f.AttachmentRows[upload.AttachmentId].Status.Should().Be(AttachmentStatus.Ready);
        f.NoteRows[note.Id].Text.Should().Be("protected");
        f.Events.Should().ContainSingle();
    }

    [Theory]
    [InlineData("../../file.txt", "file.txt")]
    [InlineData("C:\\path\\file.txt", "file.txt")]
    [InlineData("x\r\n.txt", "x.txt")]
    [InlineData("\u202efile.txt", "file.txt")]
    public void Filenames_are_normalized_without_becoming_storage_keys(string input, string expected)
        => AttachmentService.NormalizeFileName(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("/")]
    [InlineData(".")]
    public void Invalid_names_are_rejected(string input) =>
        Assert.Throws<AttachmentException>(() => AttachmentService.NormalizeFileName(input));

    [Fact]
    public void Metadata_limits_are_bounded()
    {
        Assert.Throws<AttachmentException>(() => AttachmentService.NormalizeFileName(new string('x', 1025)));
        Assert.Throws<AttachmentException>(() => AttachmentService.NormalizeFileName(new string('x', 256)));
        AttachmentService.NormalizeContentType("").Should().Be("application/octet-stream");
        AttachmentService.NormalizeContentType("TEXT/PLAIN; charset=utf-8").Should().Be("text/plain");
        Assert.Throws<AttachmentException>(() => AttachmentService.NormalizeContentType("not a MIME"));
        Assert.Throws<AttachmentException>(() => AttachmentService.NormalizeContentType("text/plain\r\n"));
        Assert.Throws<AttachmentException>(() => AttachmentService.NormalizeContentType(new string('x', 201)));
        new AttachmentOptions().IsValid().Should().BeTrue();
        new NoteOptions().IsValid().Should().BeTrue();
        new NoteOptions { MaxTextLength = 0 }.IsValid().Should().BeFalse();
    }

    private static async Task AssertError(Func<Task> action, string code)
        => (await Assert.ThrowsAsync<AttachmentException>(action)).ErrorCode.Should().Be(code);

    private static async Task AssertNoteError(Func<Task> action, string code)
        => (await Assert.ThrowsAsync<NoteException>(action)).ErrorCode.Should().Be(code);
}
