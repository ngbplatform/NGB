using Dapper;
using Npgsql;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

public sealed class PostgresOperationalRegisterRepository(IUnitOfWork uow) : IOperationalRegisterRepository
{
    public async Task<IReadOnlyList<OperationalRegisterAdminItem>> GetAllAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id   AS "RegisterId",
                               code          AS "Code",
                               code_norm     AS "CodeNorm",
                               table_code    AS "TableCode",
                               name          AS "Name",
                               has_movements AS "HasMovements",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM operational_registers
                           ORDER BY code_norm;
                           """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<OperationalRegisterAdminItemRow>(cmd)).AsList();

        return rows.Select(x => x.ToItem()).ToArray();
    }

    public async Task<OperationalRegisterAdminItem?> GetByIdAsync(Guid registerId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id   AS "RegisterId",
                               code          AS "Code",
                               code_norm     AS "CodeNorm",
                               table_code    AS "TableCode",
                               name          AS "Name",
                               has_movements AS "HasMovements",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM operational_registers
                           WHERE register_id = @RegisterId
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<OperationalRegisterAdminItemRow>(cmd);
        return row?.ToItem();
    }

    public async Task<IReadOnlyList<OperationalRegisterAdminItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> registerIds,
        CancellationToken ct = default)
    {
        if (registerIds is null)
            throw new NgbArgumentRequiredException(nameof(registerIds));

        if (registerIds.Count == 0)
            return [];

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id   AS "RegisterId",
                               code          AS "Code",
                               code_norm     AS "CodeNorm",
                               table_code    AS "TableCode",
                               name          AS "Name",
                               has_movements AS "HasMovements",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM operational_registers
                           WHERE register_id = ANY(@RegisterIds)
                           ORDER BY code_norm;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterIds = registerIds.Distinct().ToArray() },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<OperationalRegisterAdminItemRow>(cmd)).AsList();
        return rows.Select(x => x.ToItem()).ToArray();
    }

    public async Task<OperationalRegisterAdminItem?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var codeNorm = NormalizeCodeNorm(code);

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id   AS "RegisterId",
                               code          AS "Code",
                               code_norm     AS "CodeNorm",
                               table_code    AS "TableCode",
                               name          AS "Name",
                               has_movements AS "HasMovements",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM operational_registers
                           WHERE code_norm = @CodeNorm
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { CodeNorm = codeNorm },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<OperationalRegisterAdminItemRow>(cmd);
        return row?.ToItem();
    }

    public async Task<OperationalRegisterAdminItem?> GetByTableCodeAsync(string tableCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tableCode))
            throw new NgbArgumentRequiredException(nameof(tableCode));

        tableCode = tableCode.Trim();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id   AS "RegisterId",
                               code          AS "Code",
                               code_norm     AS "CodeNorm",
                               table_code    AS "TableCode",
                               name          AS "Name",
                               has_movements AS "HasMovements",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM operational_registers
                           WHERE table_code = @TableCode
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { TableCode = tableCode },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<OperationalRegisterAdminItemRow>(cmd);
        return row?.ToItem();
    }

    public async Task UpsertAsync(OperationalRegisterUpsert register, DateTime nowUtc, CancellationToken ct = default)
    {
        if (register is null)
            throw new NgbArgumentRequiredException(nameof(register));
       
        nowUtc.EnsureUtc(nameof(nowUtc));

        if (register.RegisterId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(register));

        var code = register.Code.Trim();
        var name = register.Name.Trim();

        if (code.Length == 0)
            throw new NgbArgumentRequiredException(nameof(register));

        if (name.Length == 0)
            throw new NgbArgumentRequiredException(nameof(register));

        // Registry writes are part of business transaction (atomic with rule changes, etc.).
        await uow.EnsureOpenForTransactionAsync(ct);

        var codeNorm = NormalizeCodeNorm(code);
        var tableCode = OperationalRegisterNaming.NormalizeTableCode(codeNorm);

        // Physical table name collisions are prevented by DB constraint (ux_operational_registers_table_code).
        // Runtime layer also performs a fail-fast check (see OperationalRegisterManagementService).

        const string sql = """
                           INSERT INTO operational_registers(
                               register_id,
                               code,
                               name,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @RegisterId,
                               @Code,
                               @Name,
                               @NowUtc,
                               @NowUtc
                           )
                           ON CONFLICT (register_id) DO UPDATE
                           SET code = EXCLUDED.code,
                               name = EXCLUDED.name,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                register.RegisterId,
                Code = code,
                Name = name,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        try
        {
            await uow.Connection.ExecuteAsync(cmd);
        }
        catch (PostgresException ex) when (IsTableCodeUniqueViolation(ex))
        {
            const string lookupSql = """
SELECT
    register_id AS "RegisterId",
    code        AS "Code",
    code_norm   AS "CodeNorm"
FROM operational_registers
WHERE table_code = 
LIMIT 1;
""";

            var existing = await uow.Connection.QuerySingleOrDefaultAsync<CollisionRow>(
                new CommandDefinition(
                    lookupSql,
                    new { TableCode = tableCode },
                    transaction: uow.Transaction,
                    cancellationToken: ct));

            if (existing is null)
            {
                throw new NgbInvariantViolationException(
                    "Operational register table_code unique violation, but the conflicting row was not found.",
                    new Dictionary<string, object?>
                    {
                        ["code"] = code,
                        ["codeNorm"] = codeNorm,
                        ["tableCode"] = tableCode
                    },
                    ex);
            }

            throw new OperationalRegisterTableCodeCollisionException(
                code,
                codeNorm,
                tableCode,
                existing.RegisterId,
                existing.Code,
                existing.CodeNorm);
        }
    }

    private static bool IsTableCodeUniqueViolation(PostgresException ex)
        => ex.SqlState == PostgresErrorCodes.UniqueViolation
           && (string.Equals(ex.ConstraintName, "ux_operational_registers_table_code", StringComparison.Ordinal)
               || string.Equals(ex.ConstraintName, "operational_registers_table_code_key", StringComparison.Ordinal));

    private static string NormalizeCodeNorm(string code) => code.Trim().ToLowerInvariant();

    private sealed class CollisionRow
    {
        public Guid RegisterId { get; init; }
        public string Code { get; init; } = null!;
        public string CodeNorm { get; init; } = null!;
    }
}
