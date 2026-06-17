namespace NGB.Core.Security;

public sealed record PlatformUserAccessState(
    Guid UserId,
    string AuthSubject,
    string? Email,
    string? DisplayName,
    bool IsActive,
    long AccessVersion);
