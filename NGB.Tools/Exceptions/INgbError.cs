namespace NGB.Tools.Exceptions;

/// <summary>
/// Standardized NGB error contract.
///
/// Goals:
/// - Stable error codes for programmatic handling.
/// - Small structured context map for diagnostics and UX.
/// - No coupling to transport layer (HTTP/gRPC).
/// </summary>
public interface INgbError
{
    string ErrorCode { get; }

    NgbErrorKind Kind { get; }

    IReadOnlyDictionary<string, object?> Context { get; }
}
