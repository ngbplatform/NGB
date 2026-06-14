using NGB.Core.Security;

namespace NGB.Persistence.Security;

public interface IUserProvisioningOperationRepository
{
    Task<UserProvisioningOperation> UpsertAsync(
        Guid operationId,
        string operationType,
        string? requestedEmail,
        string? keycloakUserId,
        Guid? platformUserId,
        string status,
        string? error,
        Guid? requestedByUserId,
        CancellationToken ct = default);

    Task<UserProvisioningOperation?> GetByIdAsync(Guid operationId, CancellationToken ct = default);
}
