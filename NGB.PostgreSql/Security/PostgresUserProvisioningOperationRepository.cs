using Dapper;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Security;

public sealed class PostgresUserProvisioningOperationRepository(IUnitOfWork uow, TimeProvider timeProvider)
    : IUserProvisioningOperationRepository
{
    public async Task<UserProvisioningOperation> UpsertAsync(
        Guid operationId,
        string operationType,
        string? requestedEmail,
        string? keycloakUserId,
        Guid? platformUserId,
        string status,
        string? error,
        Guid? requestedByUserId,
        CancellationToken ct = default)
    {
        operationId.EnsureRequired(nameof(operationId));

        if (string.IsNullOrWhiteSpace(operationType))
            throw new ArgumentException("Provisioning operation type is required.", nameof(operationType));

        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Provisioning operation status is required.", nameof(status));

        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           INSERT INTO platform_user_provisioning_operations
                           (
                               operation_id,
                               operation_type,
                               requested_email,
                               keycloak_user_id,
                               platform_user_id,
                               status,
                               error,
                               requested_by_user_id,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES
                           (
                               @OperationId,
                               @OperationType,
                               @RequestedEmail,
                               @KeycloakUserId,
                               @PlatformUserId,
                               @Status,
                               @Error,
                               @RequestedByUserId,
                               @NowUtc,
                               @NowUtc
                           )
                           ON CONFLICT (operation_id)
                           DO UPDATE SET
                               operation_type = EXCLUDED.operation_type,
                               requested_email = EXCLUDED.requested_email,
                               keycloak_user_id = EXCLUDED.keycloak_user_id,
                               platform_user_id = EXCLUDED.platform_user_id,
                               status = EXCLUDED.status,
                               error = EXCLUDED.error,
                               requested_by_user_id = EXCLUDED.requested_by_user_id,
                               updated_at_utc = EXCLUDED.updated_at_utc
                           RETURNING
                               operation_id AS OperationId,
                               operation_type AS OperationType,
                               requested_email AS RequestedEmail,
                               keycloak_user_id AS KeycloakUserId,
                               platform_user_id AS PlatformUserId,
                               status AS Status,
                               error AS Error,
                               requested_by_user_id AS RequestedByUserId,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                OperationId = operationId,
                OperationType = operationType.Trim(),
                RequestedEmail = string.IsNullOrWhiteSpace(requestedEmail) ? null : requestedEmail.Trim(),
                KeycloakUserId = string.IsNullOrWhiteSpace(keycloakUserId) ? null : keycloakUserId.Trim(),
                PlatformUserId = platformUserId,
                Status = status.Trim(),
                Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim(),
                RequestedByUserId = requestedByUserId,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleAsync<UserProvisioningOperation>(cmd);
    }

    public async Task<UserProvisioningOperation?> GetByIdAsync(Guid operationId, CancellationToken ct = default)
    {
        operationId.EnsureRequired(nameof(operationId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               operation_id AS OperationId,
                               operation_type AS OperationType,
                               requested_email AS RequestedEmail,
                               keycloak_user_id AS KeycloakUserId,
                               platform_user_id AS PlatformUserId,
                               status AS Status,
                               error AS Error,
                               requested_by_user_id AS RequestedByUserId,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_user_provisioning_operations
                           WHERE operation_id = @OperationId;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { OperationId = operationId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<UserProvisioningOperation>(cmd);
    }
}
