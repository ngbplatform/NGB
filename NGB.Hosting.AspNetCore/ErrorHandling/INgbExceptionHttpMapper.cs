using NGB.Tools.Exceptions;

namespace NGB.Hosting.AspNetCore.ErrorHandling;

/// <summary>
/// Extensibility point for translating provider-specific exceptions into the
/// canonical HTTP error envelope without coupling shared hosting to provider SDKs.
/// </summary>
public interface INgbExceptionHttpMapper
{
    NgbExceptionHttpMapping? TryMap(Exception exception);
}

public sealed record NgbExceptionHttpMapping(
    int StatusCode,
    string ErrorCode,
    NgbErrorKind Kind,
    IReadOnlyDictionary<string, object?>? Context = null);
