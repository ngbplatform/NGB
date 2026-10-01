using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Attachments;
using NGB.Core.AuditLog;
using NGB.Persistence.Attachments;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;

namespace NGB.Runtime.Attachments;

internal sealed class AttachmentMaintenance(
    IAttachmentRepository repository,
    IAttachmentObjectStorage storage,
    IUnitOfWork uow,
    IOutboxEventRepository outbox,
    AttachmentCleanupQueue queue,
    IAuditLogService audit,
    TimeProvider clock,
    IOptions<AttachmentOptions> options,
    ILogger<AttachmentMaintenance> logger)
    : IAttachmentMaintenance
{
    public Task<int> ExpirePendingAsync(CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var rows = await repository.LockStalePendingAsync(
                now - options.Value.PendingStaleAge, 
                options.Value.CleanupBatchSize,
                token);

            foreach (var row in rows)
            {
                var deleted = row with
                {
                    Status = AttachmentStatus.Deleted,
                    DeletedAtUtc = now
                };

                await repository.SaveAsync(deleted, token);
                await queue.EnqueueAsync(deleted, false, token);
                await audit.WriteAsync(
                    AuditEntityKind.Attachment,
                    row.Id,
                    "attachments.upload_expired",
                    metadata: new
                    {
                        row.Target
                    },
                    ct: token);
            }

            if (rows.Count > 0)
                logger.LogInformation("Expired {Count} abandoned attachment uploads.", rows.Count);

            return rows.Count;
        }, ct);

    public async Task<int> ProcessCleanupAsync(CancellationToken ct)
    {
        var items = await uow.ExecuteInUowTransactionAsync(token => outbox.ClaimBatchAsync(
            AttachmentCleanupQueue.ConsumerCode,
            Math.Min(options.Value.CleanupBatchSize, 5),
            clock.GetUtcNow().UtcDateTime,
            token),
            ct);

        foreach (var item in items)
        {
            try
            {
                if (item.Event.EventType != AttachmentCleanupQueue.EventType || item.Event.SchemaVersion != 1)
                    throw new JsonException("Unsupported attachment cleanup event.");

                var payload = JsonSerializer.Deserialize<AttachmentCleanupPayload>(item.Event.PayloadJson)
                    ?? throw new JsonException("Missing attachment cleanup payload.");

                if (payload.NotBeforeUtc > clock.GetUtcNow().UtcDateTime)
                {
                    await RetryAsync(item, payload.NotBeforeUtc, "Awaiting upload URL expiry.", false, ct);
                    continue;
                }

                var row = await repository.GetAsync(payload.AttachmentId, false, ct);
                if (row is not null)
                {
                    if (!payload.StagingOnly && row.Status != AttachmentStatus.Deleted)
                        throw new InvalidOperationException("Cleanup requires a deleted attachment.");

                    await storage.DeleteObjectAsync(row.UploadObjectKey, ct);

                    if (!payload.StagingOnly)
                        await storage.DeleteObjectAsync(row.StorageObjectKey, ct);
                }

                await uow.ExecuteInUowTransactionAsync(async token =>
                {
                    if (row is not null && !payload.StagingOnly)
                    {
                        var locked = await repository.GetAsync(row.Id, true, token);
                        if (locked is not null)
                            await repository.SaveAsync(locked with { StorageDeletedAtUtc = clock.GetUtcNow().UtcDateTime }, token);
                    }

                    await outbox.MarkCompletedAsync(
                        item.Event.EventId,
                        item.ConsumerCode,
                        item.AttemptCount,
                        clock.GetUtcNow().UtcDateTime,
                        token);
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Only safe exception type is logged; SDK exceptions can contain credentials in URLs.
                logger.LogWarning(
                    "Attachment cleanup failed for event {EventId}, attempt {Attempt}, error {ErrorType}.",
                    item.Event.EventId,
                    item.AttemptCount,
                    ex.GetType().Name);

                var deadLetter = item.AttemptCount >= 8;
                var delay = TimeSpan.FromSeconds(Math.Min(900, Math.Pow(2, Math.Clamp(item.AttemptCount, 1, 10))));

                await RetryAsync(
                    item, deadLetter
                        ? null 
                        : clock.GetUtcNow().UtcDateTime.Add(delay),
                    ex.GetType().Name,
                    deadLetter, ct);
            }
        }

        return items.Count;
    }

    private Task RetryAsync(
        OutboxConsumerWorkItem item,
        DateTime? next,
        string error,
        bool deadLetter,
        CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(token => 
            outbox.MarkFailedAsync(
                item.Event.EventId,
                item.ConsumerCode,
                item.AttemptCount,
                clock.GetUtcNow().UtcDateTime,
                next,
                error,
                deadLetter,
                token),
            ct);
}
