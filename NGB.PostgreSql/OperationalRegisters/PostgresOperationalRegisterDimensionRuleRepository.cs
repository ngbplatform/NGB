using Dapper;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

public sealed class PostgresOperationalRegisterDimensionRuleRepository(IUnitOfWork uow)
    : IOperationalRegisterDimensionRuleRepository
{
    public async Task<IReadOnlyList<OperationalRegisterDimensionRule>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var sql = PostgresRegisterDimensionRulesSql.SelectRulesSql(PostgresRegisterDimensionRulesSql.OperationalRegisterDimensionRulesTable);

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
        IReadOnlyList<OperationalRegisterDimensionRule> rules,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (rules is null)
            throw new NgbArgumentRequiredException(nameof(rules));
        
        nowUtc.EnsureUtc(nameof(nowUtc));

        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        await uow.EnsureOpenForTransactionAsync(ct);

        // We base immutability decisions on the register's has_movements flag.
        // This is cheaper and more robust than probing per-register movement tables.
        var hasMovements = await GetHasMovementsFlagAsync(registerId, ct);

        // If the register has no movements, we can safely do a full replace (DELETE + INSERT).
        // Once movements exist (append-only + storno), we must avoid DELETE and avoid changing identifiers.
        if (!hasMovements)
        {
            const string deleteSql = """
                                     DELETE FROM operational_register_dimension_rules
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
        }
        else
        {
            // has_movements = true:
            // - inserts are allowed only for optional rules (IsRequired=false);
            // - deletes are forbidden by DB trigger;
            // - we do not perform updates here.
            // Therefore, we apply a non-destructive insert-only semantics.
            if (rules.Count == 0)
            {
                var existingCount = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(1) FROM operational_register_dimension_rules WHERE register_id = @RegisterId;",
                        new { RegisterId = registerId },
                        transaction: uow.Transaction,
                        cancellationToken: ct));

                if (existingCount > 0)
                {
                    throw new OperationalRegisterDimensionRulesAppendOnlyViolationException(
                        registerId,
                        reason: "replace_empty",
                        details: new Dictionary<string, object?> { ["existingCount"] = existingCount });
                }

                return;
            }
        }

        // Validate and de-duplicate by DimensionId (PK is (register_id, dimension_id)).
        var unique = new Dictionary<Guid, OperationalRegisterDimensionRule>(capacity: rules.Count);

        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];

            if (string.IsNullOrWhiteSpace(r.DimensionCode))
            {
                throw new OperationalRegisterDimensionRulesValidationException(
                    registerId,
                    reason: "empty_dimension_code",
                    details: new Dictionary<string, object?> { ["index"] = i });
            }

            // Normalize the stored code to satisfy DB trimming constraints and keep deterministic IDs meaningful.
            r = r with { DimensionCode = r.DimensionCode.Trim() };

            if (r.DimensionCode.Length == 0)
            {
                throw new OperationalRegisterDimensionRulesValidationException(
                    registerId,
                    reason: "empty_dimension_code",
                    details: new Dictionary<string, object?> { ["index"] = i });
            }

            if (r.DimensionId == Guid.Empty)
            {
                throw new OperationalRegisterDimensionRulesValidationException(
                    registerId,
                    reason: "empty_dimension_id",
                    details: new Dictionary<string, object?> { ["index"] = i });
            }

            if (r.Ordinal <= 0)
            {
                throw new OperationalRegisterDimensionRulesValidationException(
                    registerId,
                    reason: "non_positive_ordinal",
                    details: new Dictionary<string, object?> { ["index"] = i, ["ordinal"] = r.Ordinal });
            }

            if (unique.TryGetValue(r.DimensionId, out var existing))
            {
                if (existing.Ordinal != r.Ordinal
                    || existing.IsRequired != r.IsRequired
                    || !string.Equals(existing.DimensionCode, r.DimensionCode, StringComparison.Ordinal))
                {
                    throw new OperationalRegisterDimensionRulesValidationException(
                        registerId,
                        reason: "duplicate_dimension_id_conflict",
                        details: new Dictionary<string, object?>
                        {
                            ["dimensionId"] = r.DimensionId,
                            ["existingOrdinal"] = existing.Ordinal,
                            ["incomingOrdinal"] = r.Ordinal,
                            ["existingIsRequired"] = existing.IsRequired,
                            ["incomingIsRequired"] = r.IsRequired,
                            ["existingDimensionCode"] = existing.DimensionCode,
                            ["incomingDimensionCode"] = r.DimensionCode
                        });
                }

                continue;
            }

            unique.Add(r.DimensionId, r);
        }

        var ordinalCollisions = unique.Values
            .GroupBy(r => r.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: [{string.Join(", ", g.Select(x => x.DimensionId))}]")
            .OrderBy(x => x)
            .ToArray();

        if (ordinalCollisions.Length > 0)
        {
            throw new OperationalRegisterDimensionRulesValidationException(
                registerId,
                reason: "duplicate_ordinal",
                details: new Dictionary<string, object?> { ["collisions"] = ordinalCollisions });
        }

        var count = unique.Count;
        var dimensionIds = new Guid[count];
        var dimensionCodes = new string[count];
        var dimensionNames = new string[count];
        var ordinals = new int[count];
        var isRequired = new bool[count];

        var index = 0;
        foreach (var (_, r) in unique)
        {
            dimensionIds[index] = r.DimensionId;
            dimensionCodes[index] = r.DimensionCode;
            dimensionNames[index] = r.DimensionCode;
            ordinals[index] = r.Ordinal;
            isRequired[index] = r.IsRequired;
            index++;
        }

        // IMPORTANT:
        // When has_movements=true, operational_register_dimension_rules become append-only.
        // We must never attempt to INSERT existing rules again because BEFORE INSERT triggers execute
        // even for rows that would be skipped by ON CONFLICT DO NOTHING.
        // This matters for required rules that may already exist before movements (allowed),
        // but cannot be inserted after movements.
        if (hasMovements)
        {
            var existingIds = (await uow.Connection.QueryAsync<Guid>(
                new CommandDefinition(
                    "SELECT dimension_id FROM operational_register_dimension_rules WHERE register_id = @RegisterId;",
                    new { RegisterId = registerId },
                    transaction: uow.Transaction,
                    cancellationToken: ct))).ToHashSet();

            var added = unique.Values
                .Where(r => !existingIds.Contains(r.DimensionId))
                .ToArray();

            if (added.Length == 0)
                return;

            var addedRequired = added
                .Where(r => r.IsRequired)
                .Select(r => r.DimensionId)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            if (addedRequired.Length > 0)
            {
                throw new OperationalRegisterDimensionRulesAppendOnlyViolationException(
                    registerId,
                    reason: "add_required",
                    details: new Dictionary<string, object?> { ["dimensionIds"] = addedRequired });
            }

            // Replace unique with only the newly added rules.
            unique = added.ToDictionary(x => x.DimensionId, x => x);
            count = unique.Count;
            dimensionIds = new Guid[count];
            dimensionCodes = new string[count];
            dimensionNames = new string[count];
            ordinals = new int[count];
            isRequired = new bool[count];

            index = 0;
            foreach (var (_, r) in unique)
            {
                dimensionIds[index] = r.DimensionId;
                dimensionCodes[index] = r.DimensionCode;
                dimensionNames[index] = r.DimensionCode;
                ordinals[index] = r.Ordinal;
                isRequired[index] = r.IsRequired;
                index++;
            }
        }
        // Ensure referenced dimensions exist (FK in operational_register_dimension_rules).
        // This mirrors ChartOfAccounts behavior: dimension rules are allowed to introduce new dimensions.
        await PostgresRegisterDimensionRulesSql.UpsertPlatformDimensionsAsync(
            uow,
            dimensionIds,
            dimensionCodes,
            dimensionNames,
            nowUtc,
            PostgresRegisterDimensionRulesSql.PlatformDimensionsUpsertMode.UpdateCodeAndName,
            ct);
        
        await PostgresRegisterDimensionRulesSql.InsertRegisterDimensionRulesAsync(
            uow,
            PostgresRegisterDimensionRulesSql.OperationalRegisterDimensionRulesTable,
            registerId,
            dimensionIds,
            ordinals,
            isRequired,
            nowUtc,
            PostgresRegisterDimensionRulesSql.DimensionRulesConflictMode.UpdateOrdinalRequired,
            ct);
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

    private sealed class Row
    {
        public Guid DimensionId { get; init; }
        public string DimensionCode { get; init; } = null!;
        public int Ordinal { get; init; }
        public bool IsRequired { get; init; }

        public OperationalRegisterDimensionRule ToRule() => new(DimensionId, DimensionCode, Ordinal, IsRequired);
    }
}
