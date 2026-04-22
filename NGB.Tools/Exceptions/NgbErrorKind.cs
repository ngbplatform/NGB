namespace NGB.Tools.Exceptions;

/// <summary>
/// High-level error kind for mapping into transport status codes (HTTP/gRPC) and UI behavior.
/// </summary>
public enum NgbErrorKind
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Configuration = 5,
    Infrastructure = 6,
    Unknown = 100
}
