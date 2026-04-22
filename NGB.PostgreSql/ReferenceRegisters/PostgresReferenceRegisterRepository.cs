using Dapper;
using Npgsql;
using NGB.PostgreSql.UnitOfWork;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.ReferenceRegisters.Internal;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.ReferenceRegisters;

public sealed class PostgresReferenceRegisterRepository(IUnitOfWork uow) : IReferenceRegisterRepository
{
    public async Task<IReadOnlyList<ReferenceRegisterAdminItem>> GetAllAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id  AS "RegisterId",
                               code         AS "Code",
                               code_norm    AS "CodeNorm",
                               table_code   AS "TableCode",
                               name         AS "Name",
                               periodicity  AS "Periodicity",
                               record_mode  AS "RecordMode",
                               has_records  AS "HasRecords",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM reference_registers
                           ORDER BY code_norm;
                           """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<ReferenceRegisterRow>(cmd)).AsList();
        return rows.Select(x => x.ToItem()).ToArray();
    }

    public async Task<ReferenceRegisterAdminItem?> GetByIdAsync(Guid registerId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id  AS "RegisterId",
                               code         AS "Code",
                               code_norm    AS "CodeNorm",
                               table_code   AS "TableCode",
                               name         AS "Name",
                               periodicity  AS "Periodicity",
                               record_mode  AS "RecordMode",
                               has_records  AS "HasRecords",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM reference_registers
                           WHERE register_id = @RegisterId
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<ReferenceRegisterRow>(cmd);
        return row?.ToItem();
    }

    public async Task<IReadOnlyList<ReferenceRegisterAdminItem>> GetByIdsAsync(
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
                               register_id  AS "RegisterId",
                               code         AS "Code",
                               code_norm    AS "CodeNorm",
                               table_code   AS "TableCode",
                               name         AS "Name",
                               periodicity  AS "Periodicity",
                               record_mode  AS "RecordMode",
                               has_records  AS "HasRecords",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM reference_registers
                           WHERE register_id = ANY(@RegisterIds)
                           ORDER BY code_norm;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterIds = registerIds.Distinct().ToArray() },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<ReferenceRegisterRow>(cmd)).AsList();
        return rows.Select(x => x.ToItem()).ToArray();
    }

    public async Task<ReferenceRegisterAdminItem?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var codeNorm = ReferenceRegisterId.NormalizeCode(code);

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id  AS "RegisterId",
                               code         AS "Code",
                               code_norm    AS "CodeNorm",
                               table_code   AS "TableCode",
                               name         AS "Name",
                               periodicity  AS "Periodicity",
                               record_mode  AS "RecordMode",
                               has_records  AS "HasRecords",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM reference_registers
                           WHERE code_norm = @CodeNorm
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { CodeNorm = codeNorm },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<ReferenceRegisterRow>(cmd);
        return row?.ToItem();
    }

    public async Task<ReferenceRegisterAdminItem?> GetByTableCodeAsync(string tableCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tableCode))
            throw new NgbArgumentRequiredException(nameof(tableCode));

        tableCode = tableCode.Trim();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id  AS "RegisterId",
                               code         AS "Code",
                               code_norm    AS "CodeNorm",
                               table_code   AS "TableCode",
                               name         AS "Name",
                               periodicity  AS "Periodicity",
                               record_mode  AS "RecordMode",
                               has_records  AS "HasRecords",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM reference_registers
                           WHERE table_code = @TableCode
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { TableCode = tableCode },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<ReferenceRegisterRow>(cmd);
        return row?.ToItem();
    }

    public async Task UpsertAsync(ReferenceRegisterUpsert register, DateTime nowUtc, CancellationToken ct = default)
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

        var codeNorm = ReferenceRegisterId.NormalizeCode(code);
        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(codeNorm);

        const string sql = """
                           INSERT INTO reference_registers(
                               register_id,
                               code,
                               name,
                               periodicity,
                               record_mode,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @RegisterId,
                               @Code,
                               @Name,
                               @Periodicity,
                               @RecordMode,
                               @NowUtc,
                               @NowUtc
                           )
                           ON CONFLICT (register_id) DO UPDATE
                           SET code = EXCLUDED.code,
                               name = EXCLUDED.name,
                               periodicity = EXCLUDED.periodicity,
                               record_mode = EXCLUDED.record_mode,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                register.RegisterId,
                Code = code,
                Name = name,
                Periodicity = (short)register.Periodicity,
                RecordMode = (short)register.RecordMode,
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
FROM reference_registers
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
                    "Reference register table_code unique violation, but the conflicting row was not found.",
                    new Dictionary<string, object?>
                    {
                        ["code"] = code,
                        ["codeNorm"] = codeNorm,
                        ["tableCode"] = tableCode
                    },
                    ex);
            }

            throw new ReferenceRegisterTableCodeCollisionException(
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
           && (string.Equals(ex.ConstraintName, "ux_reference_registers_table_code", StringComparison.Ordinal)
               || string.Equals(ex.ConstraintName, "reference_registers_table_code_key", StringComparison.Ordinal));

    private sealed class CollisionRow
    {
        public Guid RegisterId { get; init; }
        public string Code { get; init; } = null!;
        public string CodeNorm { get; init; } = null!;
    }
}
