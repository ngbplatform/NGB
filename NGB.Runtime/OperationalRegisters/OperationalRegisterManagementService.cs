using Microsoft.Extensions.Logging;
using NGB.Core.AuditLog;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.OperationalRegisters;

public sealed class OperationalRegisterManagementService(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterDimensionRuleRepository dimensionRulesRepo,
    IOperationalRegisterResourceRepository resourcesRepo,
    IAuditLogService audit,
    ILogger<OperationalRegisterManagementService> logger,
    TimeProvider timeProvider)
    : IOperationalRegisterManagementService
{
    public async Task<Guid> UpsertAsync(string code, string name, CancellationToken ct = default)
    {
        if (code is null)
            throw new NgbArgumentRequiredException(nameof(code));

        if (name is null)
            throw new NgbArgumentRequiredException(nameof(name));

        code = code.Trim();
        name = name.Trim();

        if (code.Length == 0)
            throw new NgbArgumentRequiredException(nameof(code));

        if (name.Length == 0)
            throw new NgbArgumentRequiredException(nameof(name));

        var registerId = OperationalRegisterId.FromCode(code);
        var codeNorm = OperationalRegisterId.NormalizeCode(code);
        var tableCode = OperationalRegisterNaming.NormalizeTableCode(codeNorm);
        var nowUtc = timeProvider.GetUtcNowDateTime();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // Fail-fast on physical table name collisions (table_code).
            // Different code_norm values can normalize to the same physical table name token (e.g. "a-b" and "a_b" => "a_b").
            // Without this guard, two different registers could silently write into the same per-register tables.
            var collision = await registers.GetByTableCodeAsync(tableCode, innerCt);
            if (collision is not null && collision.RegisterId != registerId)
                throw new OperationalRegisterTableCodeCollisionException(code, codeNorm, tableCode, collision.RegisterId, collision.Code, collision.CodeNorm);

            var current = await registers.GetByIdAsync(registerId, innerCt);

            if (current is null)
            {
                await registers.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, innerCt);

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.OperationalRegister,
                    entityId: registerId,
                    actionCode: AuditActionCodes.OperationalRegisterUpsert,
                    changes:
                    [
                        AuditLogService.Change("code", null, code),
                        AuditLogService.Change("name", null, name),
                        AuditLogService.Change("table_code", null, tableCode),
                    ],
                    metadata: new { code, name, tableCode },
                    ct: innerCt);

                logger.LogInformation("Created operational register {RegisterId} ({Code})", registerId, code);
                return;
            }

            // Prevent accidental "rename code to a different normalized code" while keeping the same register id.
            // This keeps deterministic IDs meaningful.
            var expectedCodeNorm = OperationalRegisterId.NormalizeCode(code);
            if (!string.Equals(expectedCodeNorm, current.CodeNorm, StringComparison.Ordinal))
                throw new OperationalRegisterCodeNormMismatchException(registerId, code, expectedCodeNorm, current.Code, current.CodeNorm);

            var changes = new List<AuditFieldChange>();

            if (!string.Equals(current.Code, code, StringComparison.Ordinal))
                changes.Add(AuditLogService.Change("code", current.Code, code));

            if (!string.Equals(current.Name, name, StringComparison.Ordinal))
                changes.Add(AuditLogService.Change("name", current.Name, name));

            if (changes.Count == 0)
                return; // strict no-op

            await registers.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.OperationalRegister,
                entityId: registerId,
                actionCode: AuditActionCodes.OperationalRegisterUpsert,
                changes: changes,
                metadata: new { code, name, tableCode },
                ct: innerCt);

            logger.LogInformation("Updated operational register {RegisterId} ({Code})", registerId, code);
        }, ct);

        return registerId;
    }

    public async Task ReplaceDimensionRulesAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterDimensionRule> newRules,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (newRules is null)
            throw new NgbArgumentRequiredException(nameof(newRules));

        ValidateRules(registerId, newRules);

        var nowUtc = timeProvider.GetUtcNowDateTime();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // Fail fast if the register does not exist.
            var currentReg = await registers.GetByIdAsync(registerId, innerCt)
                             ?? throw new OperationalRegisterNotFoundException(registerId);

            var current = await dimensionRulesRepo.GetByRegisterIdAsync(registerId, innerCt);
            if (RulesEquivalent(current, newRules))
                return; // strict no-op

            // Once ANY movements exist, dimension rules become append-only.
            // We allow forward-only evolution by adding optional dimensions (IsRequired=false),
            // but forbid destructive or tightening changes that would invalidate historical movements.
            if (currentReg.HasMovements)
                EnforceAppendOnlyDimensionRules(registerId, current, newRules);

            await dimensionRulesRepo.ReplaceAsync(registerId, newRules, nowUtc, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.OperationalRegister,
                entityId: registerId,
                actionCode: AuditActionCodes.OperationalRegisterReplaceDimensionRules,
                changes:
                [
                    AuditLogService.Change(
                        "dimension_rules",
                        ToAuditRules(current),
                        ToAuditRules(newRules))
                ],
                metadata: new { code = currentReg.Code, name = currentReg.Name, tableCode = currentReg.TableCode },
                ct: innerCt);

            logger.LogInformation("Replaced operational register {RegisterId} dimension rules ({Code})", registerId, currentReg.Code);
        }, ct);
    }

    public async Task ReplaceResourcesAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (resources is null)
            throw new NgbArgumentRequiredException(nameof(resources));

        ValidateResources(registerId, resources);

        var nowUtc = timeProvider.GetUtcNowDateTime();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var currentReg = await registers.GetByIdAsync(registerId, innerCt)
                             ?? throw new OperationalRegisterNotFoundException(registerId);

            var current = await resourcesRepo.GetByRegisterIdAsync(registerId, innerCt);
            if (ResourcesEquivalent(current, resources))
                return; // strict no-op

            await resourcesRepo.ReplaceAsync(registerId, resources, nowUtc, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.OperationalRegister,
                entityId: registerId,
                actionCode: AuditActionCodes.OperationalRegisterReplaceResources,
                changes:
                [
                    AuditLogService.Change(
                        "resources",
                        ToAuditResources(current),
                        ToAuditResources(resources))
                ],
                metadata: new { code = currentReg.Code, name = currentReg.Name, tableCode = currentReg.TableCode },
                ct: innerCt);

            logger.LogInformation("Replaced operational register {RegisterId} resources ({Code})", registerId, currentReg.Code);
        }, ct);
    }

    private sealed record AuditRule(Guid DimensionId, int Ordinal, bool IsRequired);

    private sealed record AuditResource(string CodeNorm, string ColumnCode, string Name, int Ordinal);

    private static IReadOnlyList<AuditRule> ToAuditRules(IReadOnlyList<OperationalRegisterDimensionRule> rules)
        => rules
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionId)
            .Select(x => new AuditRule(x.DimensionId, x.Ordinal, x.IsRequired))
            .ToArray();

    private static IReadOnlyList<AuditResource> ToAuditResources(IReadOnlyList<OperationalRegisterResource> resources)
        => resources
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .Select(x => new AuditResource(x.CodeNorm, x.ColumnCode, x.Name, x.Ordinal))
            .ToArray();

    private static IReadOnlyList<AuditResource> ToAuditResources(IReadOnlyList<OperationalRegisterResourceDefinition> resources)
        => resources
            .Select(r =>
            {
                var code = r.Code.Trim();
                var name = r.Name.Trim();

                return new AuditResource(
                    CodeNorm: OperationalRegisterId.NormalizeCode(code),
                    ColumnCode: OperationalRegisterNaming.NormalizeColumnCode(code),
                    Name: name,
                    Ordinal: r.Ordinal);
            })
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .ToArray();

    private static bool RulesEquivalent(
        IReadOnlyList<OperationalRegisterDimensionRule> a,
        IReadOnlyList<OperationalRegisterDimensionRule> b)
    {
        if (a.Count != b.Count)
            return false;

        var aa = a
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionId)
            .ToArray();
        
        var bb = b
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionId)
            .ToArray();

        for (var i = 0; i < aa.Length; i++)
        {
            if (aa[i].DimensionId != bb[i].DimensionId)
                return false;

            if (aa[i].Ordinal != bb[i].Ordinal)
                return false;

            if (aa[i].IsRequired != bb[i].IsRequired)
                return false;
        }

        return true;
    }

    private static bool ResourcesEquivalent(
        IReadOnlyList<OperationalRegisterResource> current,
        IReadOnlyList<OperationalRegisterResourceDefinition> proposed)
    {
        if (current.Count != proposed.Count)
            return false;

        var a = current
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .ToArray();

        var b = proposed
            .Select(r =>
            {
                var code = r.Code.Trim();
                var name = r.Name.Trim();

                return new
                {
                    Code = code,
                    CodeNorm = OperationalRegisterId.NormalizeCode(code),
                    ColumnCode = OperationalRegisterNaming.NormalizeColumnCode(code),
                    Name = name,
                    r.Ordinal
                };
            })
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .ToArray();

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].Code, b[i].Code, StringComparison.Ordinal))
                return false;

            if (!string.Equals(a[i].CodeNorm, b[i].CodeNorm, StringComparison.Ordinal))
                return false;

            if (!string.Equals(a[i].ColumnCode, b[i].ColumnCode, StringComparison.Ordinal))
                return false;

            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal))
                return false;

            if (a[i].Ordinal != b[i].Ordinal)
                return false;
        }

        return true;
    }

    private static void ValidateRules(Guid registerId, IReadOnlyList<OperationalRegisterDimensionRule> rules)
    {
        var dimIds = new HashSet<Guid>();
        foreach (var r in rules)
        {
            if (r.DimensionId == Guid.Empty)
                throw new OperationalRegisterDimensionRulesValidationException(registerId, reason: "empty_dimension_id");

            if (r.Ordinal <= 0)
                throw new OperationalRegisterDimensionRulesValidationException(registerId, reason: "non_positive_ordinal");

            if (!dimIds.Add(r.DimensionId))
                throw new OperationalRegisterDimensionRulesValidationException(
                    registerId,
                    reason: "duplicate_dimension_id",
                    details: new Dictionary<string, object?> { ["dimensionId"] = r.DimensionId });
        }

        // Ordinal must be unique within a register.
        var ordinalCollisions = rules
            .GroupBy(r => r.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                Ordinal = g.Key,
                DimensionIds = g
                    .Select(x => x.DimensionId)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray()
            })
            .OrderBy(x => x.Ordinal)
            .ToArray();

        if (ordinalCollisions.Length > 0)
        {
            throw new OperationalRegisterDimensionRulesValidationException(
                registerId,
                reason: "duplicate_ordinal",
                details: new Dictionary<string, object?>
                {
                    ["collisions"] = ordinalCollisions.Select(x => new { x.Ordinal, x.DimensionIds }).ToArray()
                });
        }
    }

    private static void EnforceAppendOnlyDimensionRules(
        Guid registerId,
        IReadOnlyList<OperationalRegisterDimensionRule> existing,
        IReadOnlyList<OperationalRegisterDimensionRule> proposed)
    {
        // Map proposed by DimensionId.
        var proposedByDim = proposed
            .ToDictionary(x => x.DimensionId, x => x);

        // 1) Forbid removing existing rules.
        var removed = existing
            .Where(r => !proposedByDim.ContainsKey(r.DimensionId))
            .Select(r => r.DimensionId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (removed.Length > 0)
        {
            throw new OperationalRegisterDimensionRulesAppendOnlyViolationException(
                registerId,
                reason: "remove",
                details: new Dictionary<string, object?>
                {
                    ["missingDimensionIds"] = removed
                });
        }

        // 2) Forbid modifying existing rules.
        var modified = existing
            .Select(r => (Existing: r, Proposed: proposedByDim[r.DimensionId]))
            .Where(x => x.Existing.Ordinal != x.Proposed.Ordinal || x.Existing.IsRequired != x.Proposed.IsRequired)
            .Select(x => new
            {
                x.Existing.DimensionId,
                ExistingOrdinal = x.Existing.Ordinal,
                ExistingIsRequired = x.Existing.IsRequired,
                ProposedOrdinal = x.Proposed.Ordinal,
                ProposedIsRequired = x.Proposed.IsRequired
            })
            .OrderBy(x => x.DimensionId)
            .ToArray();

        if (modified.Length > 0)
        {
            throw new OperationalRegisterDimensionRulesAppendOnlyViolationException(
                registerId,
                reason: "modify",
                details: new Dictionary<string, object?>
                {
                    ["changes"] = modified
                });
        }

        // 3) Allow adding new rules only if optional.
        var existingIds = existing.Select(x => x.DimensionId).ToHashSet();
        var addedRequired = proposed
            .Where(r => !existingIds.Contains(r.DimensionId) && r.IsRequired)
            .Select(r => r.DimensionId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (addedRequired.Length > 0)
        {
            throw new OperationalRegisterDimensionRulesAppendOnlyViolationException(
                registerId,
                reason: "add_required",
                details: new Dictionary<string, object?>
                {
                    ["requiredDimensionIds"] = addedRequired
                });
        }
    }

    private static void ValidateResources(Guid registerId, IReadOnlyList<OperationalRegisterResourceDefinition> resources)
    {
        if (resources.Count == 0)
            return;

        var nonPositiveOrdinals = resources
            .Where(r => r.Ordinal <= 0)
            .Select(r => new { r.Code, r.Ordinal })
            .ToArray();

        if (nonPositiveOrdinals.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "non_positive_ordinal",
                details: new Dictionary<string, object?>
                {
                    ["items"] = nonPositiveOrdinals
                });
        }

        var emptyCodes = resources
            .Where(r => string.IsNullOrWhiteSpace(r.Code))
            .Select(r => r.Ordinal)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (emptyCodes.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "empty_code",
                details: new Dictionary<string, object?> { ["ordinals"] = emptyCodes });
        }

        var emptyNames = resources
            .Where(r => string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.Code.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (emptyNames.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "empty_name",
                details: new Dictionary<string, object?> { ["codes"] = emptyNames });
        }

        var ordinalCollisions = resources
            .GroupBy(r => r.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                Ordinal = g.Key,
                Codes = g
                    .Select(x => x.Code.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(x => x.Ordinal)
            .ToArray();

        if (ordinalCollisions.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "duplicate_ordinal",
                details: new Dictionary<string, object?> { ["collisions"] = ordinalCollisions });
        }

        var codeNormCollisions = resources
            .Select(r => (r.Code.Trim(), CodeNorm: OperationalRegisterId.NormalizeCode(r.Code)))
            .GroupBy(x => x.CodeNorm, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                CodeNorm = g.Key,
                Codes = g
                    .Select(x => x.Item1)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(x => x.CodeNorm, StringComparer.Ordinal)
            .ToArray();

        if (codeNormCollisions.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "code_norm_collisions",
                details: new Dictionary<string, object?> { ["collisions"] = codeNormCollisions });
        }

        var columnCodeCollisions = resources
            .Select(r => (r.Code.Trim(), ColumnCode: OperationalRegisterNaming.NormalizeColumnCode(r.Code)))
            .GroupBy(x => x.ColumnCode, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                ColumnCode = g.Key,
                Codes = g
                    .Select(x => x.Item1)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(x => x.ColumnCode, StringComparer.Ordinal)
            .ToArray();

        if (columnCodeCollisions.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "column_code_collisions",
                details: new Dictionary<string, object?> { ["collisions"] = columnCodeCollisions });
        }

        // These columns exist in per-register fact tables and cannot be used by resources.
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

        var conflicts = resources
            .Select(r => OperationalRegisterNaming.NormalizeColumnCode(r.Code))
            .Where(reserved.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (conflicts.Length > 0)
        {
            throw new OperationalRegisterResourcesValidationException(
                registerId,
                reason: "reserved_column_code",
                details: new Dictionary<string, object?> { ["columnCodes"] = conflicts });
        }
    }
}
