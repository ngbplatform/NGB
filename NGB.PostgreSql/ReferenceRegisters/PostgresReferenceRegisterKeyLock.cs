using Dapper;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.ReferenceRegisters;

public sealed class PostgresReferenceRegisterKeyLock(IUnitOfWork uow) : IReferenceRegisterKeyLock
{
    public async Task LockKeyAsync(Guid registerId, Guid dimensionSetId, CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (dimensionSetId == Guid.Empty)
        {
            // Empty dimension set is a valid key. We still lock it to serialize "global" keys.
            dimensionSetId = Guid.Empty;
        }

        await uow.EnsureOpenForTransactionAsync(ct);

        var key1 = AdvisoryLockNamespaces.ReferenceRegisterKey;
        var key2 = HashToKey2(registerId, dimensionSetId);

        const string sql = "SELECT pg_advisory_xact_lock(@Key1, @Key2);";
        var cmd = new CommandDefinition(sql, new { Key1 = key1, Key2 = key2 }, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    private static int HashToKey2(Guid registerId, Guid dimensionSetId)
    {
        // FNV-1a over two GUIDs. We only need a stable 32-bit key2.
        unchecked
        {
            const uint fnvOffset = 2166136261;
            const uint fnvPrime = 16777619;

            var h = fnvOffset;

            static void MixGuid(ref uint hash, Guid g)
            {
                var bytes = g.ToByteArray();
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= fnvPrime;
                }
            }

            MixGuid(ref h, registerId);
            MixGuid(ref h, dimensionSetId);

            // Avoid key2=0 to reduce accidental collisions with other code that might treat 0 specially.
            if (h == 0) h = 1;

            return (int)h;
        }
    }
}
