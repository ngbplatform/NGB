using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.ReferenceRegisters;

/// <summary>
/// Serializes dynamic DDL for per-reference-register records tables (refreg_*__records).
/// </summary>
internal static class PostgresReferenceRegisterSchemaLock
{
    // Salt is stable and purely internal; changing it would only affect lock keys.
    private const int Salt = 0x72656672; // "refr" in ASCII-ish

    public static async Task<IAsyncDisposable> AcquireAsync(IUnitOfWork uow, Guid registerId, CancellationToken ct)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        return await PostgresSchemaLock.AcquireAsync(
            uow,
            key1: AdvisoryLockNamespaces.ReferenceRegisterSchema,
            entityId: registerId,
            salt: Salt,
            ct);
    }
}
