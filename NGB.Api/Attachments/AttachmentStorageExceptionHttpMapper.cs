using NGB.Attachments;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Tools.Exceptions;

namespace NGB.Api.Attachments;

internal sealed class AttachmentStorageExceptionHttpMapper : INgbExceptionHttpMapper
{
    public NgbExceptionHttpMapping? TryMap(Exception exception) =>
        exception is AttachmentStorageUnavailableException error
            ? new(503, error.ErrorCode, NgbErrorKind.Infrastructure)
            : null;
}
