namespace NGB.Core.Security;

public sealed record PlatformUserAccessVersion(Guid UserId, long Version, DateTime UpdatedAtUtc);
