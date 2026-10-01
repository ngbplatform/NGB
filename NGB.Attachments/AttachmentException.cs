using NGB.Tools.Exceptions;

namespace NGB.Attachments;

public sealed class AttachmentException(string code, string message, NgbErrorKind kind = NgbErrorKind.Validation)
    : NgbException(message, code, kind);

/// <summary>Safe transient error; deliberately omits provider exceptions which may contain signed URLs.</summary>
public sealed class AttachmentStorageUnavailableException()
    : NgbInfrastructureException("Attachment storage is temporarily unavailable. Please retry.", "attachments.storage_unavailable");
