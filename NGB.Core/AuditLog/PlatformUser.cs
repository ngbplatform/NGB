namespace NGB.Core.AuditLog;

public sealed record PlatformUser(
    Guid UserId,
    string AuthSubject,
    string? Email,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
