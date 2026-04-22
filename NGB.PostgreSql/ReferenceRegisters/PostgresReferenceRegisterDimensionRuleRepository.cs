using Dapper;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.UnitOfWork;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.ReferenceRegisters;

public sealed class PostgresReferenceRegisterDimensionRuleRepository(IUnitOfWork uow)
    : IReferenceRegisterDimensionRuleRepository
{
    public async Task<IReadOnlyList<ReferenceRegisterDimensionRule>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var sql = PostgresRegisterDimensionRulesSql.SelectRulesSql(PostgresRegisterDimensionRulesSql.ReferenceRegisterDimensionRulesTable);

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<Row>(cmd)).AsList();
        if (rows.Count == 0)
            return [];

        return rows.Select(x => x.ToRule()).ToArray();
    }

    public async Task ReplaceAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterDimensionRule> rules,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (rules is null)
            throw new NgbArgumentRequiredException(nameof(rules));
        
        nowUtc.EnsureUtc(nameof(nowUtc));

        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        await uow.EnsureOpenForTransactionAsync(ct);

        // We base immutability decisions on the register's has_records flag.
        // This is cheaper and more robust than probing per-register record tables.
        var hasRecords = await GetHasRecordsFlagAsync(registerId, ct);

        // If the register has no records, we can safely do a full replace (DELETE + INSERT).
        // Once any records exist, rules become append-only: only new optional dimensions may be added.
        if (!hasRecords)
        {
            const string deleteSql = """
                                     DELETE FROM reference_register_dimension_rules
                                     WHERE register_id = @RegisterId;
                                     """;

            var deleteCmd = new CommandDefinition(
                deleteSql,
                new { RegisterId = registerId },
                transaction: uow.Transaction,
                cancellationToken: ct);

            await uow.Connection.ExecuteAsync(deleteCmd);

            if (rules.Count == 0)
                return;

            await PostgresRegisterDimensionRulesSql.UpsertPlatformDimensionsAsync(
                uow, 
                rules.Select(x => x.DimensionId).ToArray(),
                rules.Select(x => x.DimensionCode).ToArray(),
                rules.Select(x => x.DimensionCode).ToArray(),
                nowUtc,
                PostgresRegisterDimensionRulesSql.PlatformDimensionsUpsertMode.DoNothing,
                ct);
            
            await PostgresRegisterDimensionRulesSql.InsertRegisterDimensionRulesAsync(
                uow,
                PostgresRegisterDimensionRulesSql.ReferenceRegisterDimensionRulesTable,
                registerId,
                rules.Select(x => x.DimensionId).ToArray(),
                rules.Select(x => x.Ordinal).ToArray(),
                rules.Select(x => x.IsRequired).ToArray(),
                nowUtc,
                PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.None,
                ct);
            
            return;
        }

        // Append-only mode: compare current vs next and only insert newly added OPTIONAL rules.
        var current = await GetByRegisterIdAsync(registerId, ct);
        if (current.Count == 0)
        {
            // has_records=true but no rules: allow only optional rules insertion.
            if (rules.Count == 0)
                return;

            var required = rules
                .Where(r => r.IsRequired)
                .Select(r => r.DimensionId)
                .OrderBy(x => x)
                .ToArray();
            
            if (required.Length > 0)
            {
                throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(
                    registerId,
                    reason: "add_required_dimension",
                    details: new { dimensionIds = required });
            }

            await PostgresRegisterDimensionRulesSql.UpsertPlatformDimensionsAsync(
                uow,
                rules.Select(x => x.DimensionId).ToArray(),
                rules.Select(x => x.DimensionCode).ToArray(),
                rules.Select(x => x.DimensionCode).ToArray(),
                nowUtc,
                PostgresRegisterDimensionRulesSql.PlatformDimensionsUpsertMode.DoNothing,
                ct);
            
            await PostgresRegisterDimensionRulesSql.InsertRegisterDimensionRulesAsync(
                uow,
                PostgresRegisterDimensionRulesSql.ReferenceRegisterDimensionRulesTable,
                registerId,
                rules.Select(x => x.DimensionId).ToArray(),
                rules.Select(x => x.Ordinal).ToArray(),
                rules.Select(x => x.IsRequired).ToArray(),
                nowUtc,
                PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.DoNothing,
                ct);
            
            return;
        }

        // Enforce append-only: existing rules must remain unchanged.
        var currentById = current.ToDictionary(x => x.DimensionId, x => x);
        var nextById = rules.ToDictionary(x => x.DimensionId, x => x);

        foreach (var c in currentById.Values)
        {
            if (!nextById.TryGetValue(c.DimensionId, out var n))
            {
                throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(
                    registerId,
                    reason: "remove_dimension",
                    details: new { dimensionId = c.DimensionId });
            }

            if (c.Ordinal != n.Ordinal)
            {
                throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(
                    registerId,
                    reason: "change_ordinal",
                    details: new { dimensionId = c.DimensionId, currentOrdinal = c.Ordinal, requestedOrdinal = n.Ordinal });
            }

            if (c.IsRequired != n.IsRequired)
            {
                throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(
                    registerId,
                    reason: "change_required",
                    details: new { dimensionId = c.DimensionId, currentIsRequired = c.IsRequired, requestedIsRequired = n.IsRequired });
            }
        }

        // Only new OPTIONAL rules are allowed.
        var added = rules.Where(r => !currentById.ContainsKey(r.DimensionId)).ToArray();
        if (added.Length == 0)
            return;

        var addedRequired = added
            .Where(r => r.IsRequired)
            .Select(r => r.DimensionId)
            .OrderBy(x => x)
            .ToArray();
        
        if (addedRequired.Length > 0)
        {
            throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(
                registerId,
                reason: "add_required_dimension",
                details: new { dimensionIds = addedRequired });
        }

        await PostgresRegisterDimensionRulesSql.UpsertPlatformDimensionsAsync(
            uow,
            added.Select(x => x.DimensionId).ToArray(),
            added.Select(x => x.DimensionCode).ToArray(),
            added.Select(x => x.DimensionCode).ToArray(),
            nowUtc,
            PostgresRegisterDimensionRulesSql.PlatformDimensionsUpsertMode.DoNothing,
            ct);
        
        await PostgresRegisterDimensionRulesSql.InsertRegisterDimensionRulesAsync(
            uow,
            PostgresRegisterDimensionRulesSql.ReferenceRegisterDimensionRulesTable,
            registerId,
            added.Select(x => x.DimensionId).ToArray(),
            added.Select(x => x.Ordinal).ToArray(),
            added.Select(x => x.IsRequired).ToArray(),
            nowUtc,
            PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.DoNothing,
            ct);
    }

    private async Task<bool> GetHasRecordsFlagAsync(Guid registerId, CancellationToken ct)
    {
        const string sql = """
                           SELECT has_records
                           FROM reference_registers
                           WHERE register_id = @RegisterId;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var has = await uow.Connection.ExecuteScalarAsync<bool?>(cmd);
        if (has is null)
            throw new ReferenceRegisterNotFoundException(registerId);

        return has.Value;
    }

    private sealed record Row(Guid DimensionId, string DimensionCode, int Ordinal, bool IsRequired)
    {
        public ReferenceRegisterDimensionRule ToRule() => new(DimensionId, DimensionCode, Ordinal, IsRequired);
    }
}
