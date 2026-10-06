using NGB.Contracts.BusinessObjects;

namespace NGB.Contracts.Attachments;

public sealed record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    string? CreatedByDisplayName);

public sealed record CreateAttachmentUploadRequest(
    BusinessObjectRef Target,
    string FileName,
    string ContentType,
    long SizeBytes);

public sealed record AttachmentUploadDto(
    Guid AttachmentId,
    string Url,
    DateTime ExpiresAtUtc,
    IReadOnlyDictionary<string, string> Headers);

public sealed record AttachmentDownloadDto(string Url, DateTime ExpiresAtUtc);
