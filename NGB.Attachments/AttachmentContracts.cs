using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;

namespace NGB.Attachments;

public enum AttachmentStatus
{
    PendingUpload = 1,
    Ready = 2,
    Deleted = 3
}

/// <summary>Persisted metadata. Storage keys must never be returned by the public API.</summary>
public sealed record AttachmentRecord(
    Guid Id,
    BusinessObjectRef Target,
    string FileName,
    string ContentType,
    long SizeBytes,
    string StorageObjectKey,
    string UploadObjectKey,
    AttachmentStatus Status,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    DateTime UploadExpiresAtUtc,
    DateTime? CompletedAtUtc = null,
    DateTime? DeletedAtUtc = null,
    Guid? DeletedByUserId = null,
    DateTime? StorageDeletedAtUtc = null,
    string? CreatedByDisplayName = null);

public sealed record AttachmentUploadTarget(string Url, IReadOnlyDictionary<string, string> Headers);

public sealed record AttachmentStoredObject(long SizeBytes, string ContentType, string ETag);

/// <summary>Attachment-specific object storage. Implementations must translate provider failures.</summary>
public interface IAttachmentObjectStorage
{
    Task<AttachmentUploadTarget> CreateUploadTargetAsync(
        string key,
        string contentType,
        TimeSpan lifetime,
        CancellationToken ct);

    Task<AttachmentStoredObject?> GetObjectInfoAsync(string key, CancellationToken ct);

    /// <summary>Conditionally copies the verified upload to a key unavailable to browser PUTs.</summary>
    Task SealUploadAsync(string uploadKey, string destinationKey, string expectedETag, CancellationToken ct);

    Task<string> CreateDownloadTargetAsync(string key, string fileName, TimeSpan lifetime, CancellationToken ct);

    Task DeleteObjectAsync(string key, CancellationToken ct);
}

public interface IAttachmentService
{
    Task<BusinessObjectPage<AttachmentDto>> ListAsync(
        BusinessObjectRef target,
        int limit,
        Guid? cursor,
        CancellationToken ct);

    Task<AttachmentUploadDto> CreateUploadAsync(CreateAttachmentUploadRequest request, CancellationToken ct);
    Task<AttachmentDto> CompleteAsync(Guid id, CancellationToken ct);
    Task<AttachmentDownloadDto> DownloadAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IAttachmentMaintenance
{
    Task<int> ExpirePendingAsync(CancellationToken ct);
    Task<int> ProcessCleanupAsync(CancellationToken ct);
}
