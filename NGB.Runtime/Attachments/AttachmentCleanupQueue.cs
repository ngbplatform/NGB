using System.Text.Json;
using NGB.Attachments;
using NGB.Persistence.Outbox;

namespace NGB.Runtime.Attachments;

internal sealed record AttachmentCleanupPayload(Guid AttachmentId, bool StagingOnly, DateTime NotBeforeUtc);

internal sealed class AttachmentCleanupQueue(IOutboxEventRepository outbox, TimeProvider clock)
{
    internal const string ConsumerCode = "attachments-cleanup";
    internal const string EventType = "ngb.attachment.cleanup.v1";

    public Task EnqueueAsync(AttachmentRecord row, bool stagingOnly, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Presigned PUTs can be replayed until expiry. Allow five minutes for requests already in flight.
        var notBefore = row.UploadExpiresAtUtc.AddMinutes(5);
        var payload = new AttachmentCleanupPayload(row.Id, stagingOnly, notBefore);
        var id = Guid.CreateVersion7();

        return outbox.AppendAsync(new(
            id,
            EventType,
            1,
            now,
            "ngb.attachments",
            row.Id.ToString("N"),
            row.DeletedByUserId,
            id, 
            null,
            JsonSerializer.Serialize(payload),
            now),
            [ConsumerCode],
            ct);
    }
}
