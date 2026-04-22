using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Serializes dynamic DDL for per-register tables (movements/turnovers/balances).
/// </summary>
internal static class PostgresOperationalRegisterSchemaLock
{
    // Salt is stable and purely internal; changing it would only affect lock keys.
    private const int Salt = 0x6F707265; // "opre" in ASCII-ish

    public static async Task<IAsyncDisposable> AcquireAsync(IUnitOfWork uow, Guid registerId, CancellationToken ct)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        return await PostgresSchemaLock.AcquireAsync(
            uow,
            key1: AdvisoryLockNamespaces.OperationalRegisterSchema,
            entityId: registerId,
            salt: Salt,
            ct);
    }
}
