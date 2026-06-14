namespace NGB.Core.Security;

public sealed record UserProvisioningOperation(
    Guid OperationId,
    string OperationType,
    string? RequestedEmail,
    string? KeycloakUserId,
    Guid? PlatformUserId,
    string Status,
    string? Error,
    Guid? RequestedByUserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
