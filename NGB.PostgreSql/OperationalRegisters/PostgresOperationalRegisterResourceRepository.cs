using Dapper;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.OperationalRegisters;

public sealed class PostgresOperationalRegisterResourceRepository(IUnitOfWork uow)
    : IOperationalRegisterResourceRepository
{
    public async Task<IReadOnlyList<OperationalRegisterResource>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               r.code        AS "Code",
                               r.code_norm   AS "CodeNorm",
                               r.column_code AS "ColumnCode",
                               r.name        AS "Name",
                               r.ordinal     AS "Ordinal"
                           FROM operational_register_resources r
                           WHERE r.register_id = @RegisterId
                           ORDER BY r.ordinal, r.code_norm;
                           """;

        var cmd = new CommandDefinition(sql, new { RegisterId = registerId }, cancellationToken: ct);
        var rows = await uow.Connection.QueryAsync<OperationalRegisterResource>(cmd);
        return rows.AsList();
    }

    public async Task ReplaceAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        uow.EnsureActiveTransaction();

        resources ??= [];

        // We base immutability decisions on the register's has_movements flag.
        // This is cheaper and more robust than probing per-register movement tables.
        var hasMovements = await GetHasMovementsFlagAsync(registerId, ct);

        // Existing resources (if any).
        var existing = await GetByRegisterIdAsync(registerId, ct);

        // IMPORTANT (append-only + storno):
        // Storno is implemented by appending rows copied from prior non-storno rows.
        // Therefore, resource physical columns must be stable once movements exist.
        // If a resource (column_code) disappears from metadata, the storno INSERT will stop copying it,
        // and the appended storno row would default that column to 0 — silently breaking reversals.
        //
        // We allow:
        // - adding new resources (new columns) at any time (old rows simply have 0 in new columns)
        // We disallow:
        // - removing/renaming resources (column_code changes) once ANY movements exist for this register.
        //   (If you need destructive changes, drop/recreate the register tables while you are not in production.)
        EnforceResourceImmutabilityWhenHasMovements(registerId, existing, hasMovements, resources);

        // Normalize in the repository to keep callers simple.
        var codes = resources.Select(r => r.Code).ToArray();
        var tableCodes = resources.Select(r => OperationalRegisterId.NormalizeCode(r.Code)).ToArray();
        var columnCodes = resources.Select(r => OperationalRegisterNaming.NormalizeColumnCode(r.Code)).ToArray();
        var names = resources.Select(r => r.Name).ToArray();
        var ordinals = resources.Select(r => r.Ordinal).ToArray();

        ValidateNoDuplicates(registerId, resources, tableCodes, columnCodes);
        ValidateNoReservedColumnConflicts(registerId, columnCodes);

        // If the register has no movements, we can safely do a full replace (DELETE + INSERT).
        // Once movements exist (append-only + storno), we must avoid DELETE and avoid changing identifiers.
        if (!hasMovements)
        {
            const string sql = """
                               DELETE FROM operational_register_resources WHERE register_id = @RegisterId;

                               INSERT INTO operational_register_resources
                                   (register_id, code, code_norm, column_code, name, ordinal, created_at_utc, updated_at_utc)
                               SELECT
                                   @RegisterId,
                                   x.code,
                                   x.code_norm,
                                   x.column_code,
                                   x.name,
                                   x.ordinal,
                                   @NowUtc,
                                   @NowUtc
                               FROM UNNEST(@Codes, @CodeNorms, @ColumnCodes, @Names, @Ordinals)
                                   AS x(code, code_norm, column_code, name, ordinal);
                               """;

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    RegisterId = registerId,
                    NowUtc = nowUtc,
                    Codes = codes,
                    CodeNorms = tableCodes,
                    ColumnCodes = columnCodes,
                    Names = names,
                    Ordinals = ordinals
                },
                transaction: uow.Transaction,
                cancellationToken: ct);

            await uow.Connection.ExecuteAsync(cmd);
            return;
        }

        // has_movements = true:
        // - inserts (adding new resources) are allowed;
        // - updates are allowed only for user-facing fields (name/ordinal);
        // - deletes are forbidden by DB trigger.
        // Therefore, we apply a non-destructive "upsert" style update.
        //
        // NOTE: We must also handle ordinal swaps without violating the unique (register_id, ordinal) constraint.
        // We do it in 2 phases:
        // 1) move existing rows to a temporary non-colliding ordinal range
        // 2) update existing to final ordinals/names and insert new rows

        // Partition incoming rows into existing vs added by physical column_code.
        var existingByColumn = existing.ToDictionary(x => x.ColumnCode, StringComparer.Ordinal);

        var incoming = resources.Select(r => new
        {
            r.Code,
            CodeNorm = OperationalRegisterId.NormalizeCode(r.Code),
            ColumnCode = OperationalRegisterNaming.NormalizeColumnCode(r.Code),
            r.Name,
            r.Ordinal
        }).ToArray();

        var toInsert = incoming
            .Where(x => !existingByColumn.ContainsKey(x.ColumnCode))
            .ToArray();
        
        var toUpdate = incoming
            .Where(x => existingByColumn.ContainsKey(x.ColumnCode))
            .ToArray();

        // Phase 1: move existing rows to temp ordinals to avoid swap collisions.
        if (existing.Count > 0)
        {
            var maxExistingOrdinal = existing.Max(x => x.Ordinal);
            var maxTargetOrdinal = ordinals.Length == 0 ? 0 : ordinals.Max();
            var tempBase = (long)Math.Max(maxExistingOrdinal, maxTargetOrdinal) + 1000;
            var tempCount = existing.Count;
            
            if (tempBase + tempCount > int.MaxValue)
            {
                throw new OperationalRegisterResourcesAppendOnlyViolationException(
                    registerId,
                    reason: "ordinal_overflow",
                    details: new Dictionary<string, object?>
                    {
                        ["hint"] = "Drop/recreate the database or register tables to change the schema."
                    });
            }

            var tmpColumnCodes = existing
                .OrderBy(x => x.ColumnCode, StringComparer.Ordinal)
                .Select(x => x.ColumnCode)
                .ToArray();
            
            var tmpOrdinals = tmpColumnCodes
                .Select((_, i) => (int)(tempBase + i))
                .ToArray();

            const string tmpSql = """
                                  UPDATE operational_register_resources r
                                     SET ordinal = x.tmp_ordinal,
                                         updated_at_utc = @NowUtc
                                    FROM UNNEST(@ColumnCodes, @TmpOrdinals) AS x(column_code, tmp_ordinal)
                                   WHERE r.register_id = @RegisterId
                                     AND r.column_code = x.column_code;
                                  """;

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    tmpSql,
                    new { RegisterId = registerId, NowUtc = nowUtc, ColumnCodes = tmpColumnCodes, TmpOrdinals = tmpOrdinals },
                    transaction: uow.Transaction,
                    cancellationToken: ct));
        }

        // Phase 2a: insert new resources (if any).
        if (toInsert.Length > 0)
        {
            var insCodes = toInsert.Select(x => x.Code).ToArray();
            var insCodeNorms = toInsert.Select(x => x.CodeNorm).ToArray();
            var insColumnCodes = toInsert.Select(x => x.ColumnCode).ToArray();
            var insNames = toInsert.Select(x => x.Name).ToArray();
            var insOrdinals = toInsert.Select(x => x.Ordinal).ToArray();

            const string insSql = """
                                  INSERT INTO operational_register_resources
                                      (register_id, code, code_norm, column_code, name, ordinal, created_at_utc, updated_at_utc)
                                  SELECT
                                      @RegisterId,
                                      x.code,
                                      x.code_norm,
                                      x.column_code,
                                      x.name,
                                      x.ordinal,
                                      @NowUtc,
                                      @NowUtc
                                  FROM UNNEST(@Codes, @CodeNorms, @ColumnCodes, @Names, @Ordinals)
                                      AS x(code, code_norm, column_code, name, ordinal);
                                  """;

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    insSql,
                    new
                    {
                        RegisterId = registerId,
                        NowUtc = nowUtc,
                        Codes = insCodes,
                        CodeNorms = insCodeNorms,
                        ColumnCodes = insColumnCodes,
                        Names = insNames,
                        Ordinals = insOrdinals
                    },
                    transaction: uow.Transaction,
                    cancellationToken: ct));
        }

        // Phase 2b: update existing resources to final ordinals/names.
        if (toUpdate.Length > 0)
        {
            var updColumnCodes = toUpdate.Select(x => x.ColumnCode).ToArray();
            var updNames = toUpdate.Select(x => x.Name).ToArray();
            var updOrdinals = toUpdate.Select(x => x.Ordinal).ToArray();

            const string updSql = """
                                  UPDATE operational_register_resources r
                                     SET name = x.name,
                                         ordinal = x.ordinal,
                                         updated_at_utc = @NowUtc
                                    FROM UNNEST(@ColumnCodes, @Names, @Ordinals) AS x(column_code, name, ordinal)
                                   WHERE r.register_id = @RegisterId
                                     AND r.column_code = x.column_code;
                                  """;

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    updSql,
                    new { RegisterId = registerId, NowUtc = nowUtc, ColumnCodes = updColumnCodes, Names = updNames, Ordinals = updOrdinals },
                    transaction: uow.Transaction,
                    cancellationToken: ct));
        }
    }

    private static void EnforceResourceImmutabilityWhenHasMovements(
        Guid registerId,
        IReadOnlyList<OperationalRegisterResource> existing,
        bool hasMovements,
        IReadOnlyList<OperationalRegisterResourceDefinition> newResources)
    {
        if (!hasMovements || existing.Count == 0)
            return;

        // Map incoming resources by physical column_code.
        var incomingByColumn = newResources
            .Select(r => new
            {
                r.Code,
                CodeNorm = OperationalRegisterId.NormalizeCode(r.Code),
                ColumnCode = OperationalRegisterNaming.NormalizeColumnCode(r.Code)
            })
            .ToDictionary(x => x.ColumnCode, x => x, StringComparer.Ordinal);

        // Disallow removing existing resources.
        var removed = existing
            .Select(r => r.ColumnCode)
            .Where(cc => !incomingByColumn.ContainsKey(cc))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x)
            .ToArray();

        if (removed.Length > 0)
        {
            throw new OperationalRegisterResourcesAppendOnlyViolationException(
                registerId,
                reason: "remove",
                details: new Dictionary<string, object?>
                {
                    ["removedColumnCodes"] = removed,
                    ["hint"] = "Drop/recreate the database or register tables to change the schema."
                });
        }

        // Disallow changing resource business codes (code/code_norm) while keeping the same column_code.
        // The DB trigger forbids such updates anyway; we fail-fast with a clearer message.
        var codeChanges = existing
            .Select(r => (Existing: r, Incoming: incomingByColumn[r.ColumnCode]))
            .Where(x => !string.Equals(x.Existing.CodeNorm, x.Incoming.CodeNorm, StringComparison.Ordinal))
            .Select(x => $"{x.Existing.ColumnCode}: '{x.Existing.CodeNorm}' -> '{x.Incoming.CodeNorm}'")
            .OrderBy(x => x)
            .ToArray();

        if (codeChanges.Length > 0)
        {
            throw new OperationalRegisterResourcesAppendOnlyViolationException(
                registerId,
                reason: "rename",
                details: new Dictionary<string, object?>
                {
                    ["changes"] = codeChanges,
                    ["hint"] = "Drop/recreate the database or register tables to change the schema."
                });
        }
    }

    private async Task<bool> GetHasMovementsFlagAsync(Guid registerId, CancellationToken ct)
    {
        const string sql = "SELECT has_movements FROM operational_registers WHERE register_id = @RegisterId;";
        var flag = await uow.Connection.ExecuteScalarAsync<bool?>(
            new CommandDefinition(sql, new { RegisterId = registerId }, transaction: uow.Transaction, cancellationToken: ct));

        if (flag is null)
            throw new OperationalRegisterNotFoundException(registerId);

        return flag.Value;
    }

    private static void ValidateNoDuplicates(
        Guid registerId,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources,
        IReadOnlyList<string> tableCodes,
        IReadOnlyList<string> columnCodes)
    {
        if (resources.Count == 0)
            return;

        // Extra guard: arrays should be 1:1 and in sync.
        // We normalize codes outside of this method (repository-level normalization),
        // so we validate collisions against the provided normalized arrays.
        if (tableCodes.Count != resources.Count || columnCodes.Count != resources.Count)
            throw new OperationalRegisterResourcesValidationException(registerId, reason: "normalization_inconsistent");

        var nonPositiveOrdinals = resources
            .Where(r => r.Ordinal <= 0)
            .Select(r => $"{r.Code}:{r.Ordinal}")
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (nonPositiveOrdinals.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "non_positive_ordinal",
                details: new Dictionary<string, object?> { ["items"] = nonPositiveOrdinals });
        }

        var ordinalCollisions = resources
            .GroupBy(r => r.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: [{string.Join(", ", g.Select(x => x.Code))}]")
            .OrderBy(x => x)
            .ToArray();

        if (ordinalCollisions.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "duplicate_ordinal",
                details: new Dictionary<string, object?> { ["collisions"] = ordinalCollisions });
        }

        var tableCodeCollisions = tableCodes
            .Select((codeNorm, i) => (CodeNorm: codeNorm, resources[i].Code))
            .GroupBy(x => x.CodeNorm, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: [{string.Join(", ", g.Select(x => x.Code))}]")
            .OrderBy(x => x)
            .ToArray();

        if (tableCodeCollisions.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "code_norm_collisions",
                details: new Dictionary<string, object?> { ["collisions"] = tableCodeCollisions });
        }

        var columnCodeCollisions = columnCodes
            .Select((columnCode, i) => (ColumnCode: columnCode, resources[i].Code))
            .GroupBy(x => x.ColumnCode, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: [{string.Join(", ", g.Select(x => x.Code))}]")
            .OrderBy(x => x)
            .ToArray();

        if (columnCodeCollisions.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "column_code_collisions",
                details: new Dictionary<string, object?> { ["collisions"] = columnCodeCollisions });
        }
    }

    private static void ValidateNoReservedColumnConflicts(Guid registerId, IReadOnlyList<string> columnCodes)
    {
        // These columns exist in per-register fact tables and cannot be used by resources.
        // (Otherwise INSERT column lists would become ambiguous or invalid.)
        var reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "movement_id",
            "turnover_id",
            "balance_id",
            "document_id",
            "occurred_at_utc",
            "period_month",
            "dimension_set_id",
            "is_storno"
        };

        var conflicts = columnCodes.Where(reserved.Contains).Distinct().OrderBy(x => x).ToArray();
        if (conflicts.Length == 0)
            return;

        throw new OperationalRegisterResourcesValidationException(
            registerId,
            reason: "reserved_column_code",
            details: new Dictionary<string, object?> { ["columnCodes"] = conflicts });
    }
}
