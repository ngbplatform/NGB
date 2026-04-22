using Microsoft.Extensions.Logging;
using NGB.Core.AuditLog;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.ReferenceRegisters;

public sealed class ReferenceRegisterManagementService(
    IUnitOfWork uow,
    IReferenceRegisterRepository registers,
    IReferenceRegisterDimensionRuleRepository dimensionRulesRepo,
    IReferenceRegisterFieldRepository fieldsRepo,
    IReferenceRegisterRecordsStore recordsStore,
    IAuditLogService audit,
    ILogger<ReferenceRegisterManagementService> logger,
    TimeProvider timeProvider)
    : IReferenceRegisterManagementService
{
    public async Task<Guid> UpsertAsync(
        string code,
        string name,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode,
        CancellationToken ct = default)
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

        var registerId = ReferenceRegisterId.FromCode(code);
        var codeNorm = ReferenceRegisterId.NormalizeCode(code);
        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
        var nowUtc = timeProvider.GetUtcNowDateTime();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var collision = await registers.GetByTableCodeAsync(tableCode, innerCt);
            if (collision is not null && collision.RegisterId != registerId)
            {
                throw new ReferenceRegisterTableCodeCollisionException(
                    code,
                    codeNorm,
                    tableCode,
                    collision.RegisterId,
                    collision.Code,
                    collision.CodeNorm);
            }

            var current = await registers.GetByIdAsync(registerId, innerCt);

            if (current is null)
            {
                await registers.UpsertAsync(
                    new ReferenceRegisterUpsert(registerId, code, name, periodicity, recordMode),
                    nowUtc,
                    innerCt);

                await recordsStore.EnsureSchemaAsync(registerId, innerCt);

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.ReferenceRegister,
                    entityId: registerId,
                    actionCode: AuditActionCodes.ReferenceRegisterUpsert,
                    changes:
                    [
                        AuditLogService.Change("code", null, code),
                        AuditLogService.Change("name", null, name),
                        AuditLogService.Change("table_code", null, tableCode),
                        AuditLogService.Change("periodicity", null, (short)periodicity),
                        AuditLogService.Change("record_mode", null, (short)recordMode),
                    ],
                    metadata: new { code, name, tableCode, periodicity, recordMode },
                    ct: innerCt);

                logger.LogInformation("Created reference register {RegisterId} ({Code})", registerId, code);
                return;
            }

            // Keep deterministic IDs meaningful: code_norm must not change.
            var expectedCodeNorm = ReferenceRegisterId.NormalizeCode(code);
            if (!string.Equals(expectedCodeNorm, current.CodeNorm, StringComparison.Ordinal))
            {
                throw new ReferenceRegisterCodeNormMismatchException(
                    registerId,
                    attemptedCode: code,
                    attemptedCodeNorm: expectedCodeNorm,
                    existingCode: current.Code,
                    existingCodeNorm: current.CodeNorm);
            }

            if (current.HasRecords)
            {
                if (current.Periodicity != periodicity)
                {
                    throw new ReferenceRegisterMetadataImmutabilityViolationException(
                        registerId,
                        reason: "periodicity",
                        details: new { current = current.Periodicity.ToString(), requested = periodicity.ToString() });
                }

                if (current.RecordMode != recordMode)
                {
                    throw new ReferenceRegisterMetadataImmutabilityViolationException(
                        registerId,
                        reason: "record_mode",
                        details: new { current = current.RecordMode.ToString(), requested = recordMode.ToString() });
                }
            }

            if (UpsertEquivalent(current, code, name, periodicity, recordMode, tableCode))
                return; // strict no-op

            await registers.UpsertAsync(
                new ReferenceRegisterUpsert(registerId, code, name, periodicity, recordMode),
                nowUtc,
                innerCt);

            await recordsStore.EnsureSchemaAsync(registerId, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ReferenceRegister,
                entityId: registerId,
                actionCode: AuditActionCodes.ReferenceRegisterUpsert,
                changes: BuildUpsertAuditChanges(current, code, name, periodicity, recordMode, tableCode),
                metadata: new { code, name, tableCode, periodicity, recordMode },
                ct: innerCt);

            logger.LogInformation("Updated reference register {RegisterId} ({Code})", registerId, current.Code);
        }, ct);

        return registerId;
    }

    public async Task ReplaceDimensionRulesAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterDimensionRule> dimensionRules,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (dimensionRules is null)
            throw new NgbArgumentRequiredException(nameof(dimensionRules));

        ValidateDimensionRules(registerId, dimensionRules);

        var nowUtc = timeProvider.GetUtcNowDateTime();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var currentReg = await registers.GetByIdAsync(registerId, innerCt)
                             ?? throw new ReferenceRegisterNotFoundException(registerId);

            var current = await dimensionRulesRepo.GetByRegisterIdAsync(registerId, innerCt);
            if (RulesEquivalent(current, dimensionRules))
                return; // strict no-op

            if (currentReg.HasRecords)
                EnforceAppendOnlyDimensionRules(registerId, current, dimensionRules);

            await dimensionRulesRepo.ReplaceAsync(registerId, dimensionRules, nowUtc, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ReferenceRegister,
                entityId: registerId,
                actionCode: AuditActionCodes.ReferenceRegisterReplaceDimensionRules,
                changes:
                [
                    AuditLogService.Change(
                        "dimension_rules",
                        ToAuditRules(current),
                        ToAuditRules(dimensionRules))
                ],
                metadata: new { code = currentReg.Code, name = currentReg.Name, tableCode = currentReg.TableCode },
                ct: innerCt);

            logger.LogInformation("Replaced reference register {RegisterId} dimension rules ({Code})", registerId, currentReg.Code);
        }, ct);
    }

    public async Task ReplaceFieldsAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterFieldDefinition> fields,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (fields is null)
            throw new NgbArgumentRequiredException(nameof(fields));

        ValidateFields(registerId, fields);

        var nowUtc = timeProvider.GetUtcNowDateTime();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var currentReg = await registers.GetByIdAsync(registerId, innerCt)
                             ?? throw new ReferenceRegisterNotFoundException(registerId);

            var current = await fieldsRepo.GetByRegisterIdAsync(registerId, innerCt);
            if (FieldsEquivalent(current, fields))
                return; // strict no-op

            // For now: fields are immutable once records exist.
            // (Schema evolution is handled together with per-register table DDL in RR-03/RR-04.)
            if (currentReg.HasRecords)
                throw new ReferenceRegisterMetadataImmutabilityViolationException(registerId, reason: "fields");

            await fieldsRepo.ReplaceAsync(registerId, fields, nowUtc, innerCt);

            await recordsStore.EnsureSchemaAsync(registerId, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ReferenceRegister,
                entityId: registerId,
                actionCode: AuditActionCodes.ReferenceRegisterReplaceFields,
                changes:
                [
                    AuditLogService.Change(
                        "fields",
                        ToAuditFields(current),
                        ToAuditFields(fields))
                ],
                metadata: new { code = currentReg.Code, name = currentReg.Name, tableCode = currentReg.TableCode },
                ct: innerCt);

            logger.LogInformation("Replaced reference register {RegisterId} fields ({Code})", registerId, currentReg.Code);
        }, ct);
    }

    private static bool UpsertEquivalent(
        ReferenceRegisterAdminItem current,
        string code,
        string name,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode,
        string tableCode)
    {
        return string.Equals(current.Code, code, StringComparison.Ordinal)
               && string.Equals(current.Name, name, StringComparison.Ordinal)
               && current.Periodicity == periodicity
               && current.RecordMode == recordMode
               && string.Equals(current.TableCode, tableCode, StringComparison.Ordinal);
    }

    private static IReadOnlyList<AuditFieldChange> BuildUpsertAuditChanges(
        ReferenceRegisterAdminItem current,
        string code,
        string name,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode,
        string tableCode)
    {
        var changes = new List<AuditFieldChange>(capacity: 5);

        if (!string.Equals(current.Code, code, StringComparison.Ordinal))
            changes.Add(AuditLogService.Change("code", current.Code, code));

        if (!string.Equals(current.Name, name, StringComparison.Ordinal))
            changes.Add(AuditLogService.Change("name", current.Name, name));

        if (!string.Equals(current.TableCode, tableCode, StringComparison.Ordinal))
            changes.Add(AuditLogService.Change("table_code", current.TableCode, tableCode));

        if (current.Periodicity != periodicity)
            changes.Add(AuditLogService.Change("periodicity", (short)current.Periodicity, (short)periodicity));

        if (current.RecordMode != recordMode)
            changes.Add(AuditLogService.Change("record_mode", (short)current.RecordMode, (short)recordMode));

        return changes;
    }

    private sealed record AuditRule(Guid DimensionId, string DimensionCode, int Ordinal, bool IsRequired);

    private sealed record AuditField(string CodeNorm, string ColumnCode, string Name, int Ordinal, ColumnType ColumnType, bool IsNullable);

    private static IReadOnlyList<AuditRule> ToAuditRules(IReadOnlyList<ReferenceRegisterDimensionRule> rules)
        => rules
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionId)
            .Select(x => new AuditRule(x.DimensionId, x.DimensionCode, x.Ordinal, x.IsRequired))
            .ToArray();

    private static IReadOnlyList<AuditField> ToAuditFields(IReadOnlyList<ReferenceRegisterField> fields)
        => fields
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .Select(x => new AuditField(x.CodeNorm, x.ColumnCode, x.Name, x.Ordinal, x.ColumnType, x.IsNullable))
            .ToArray();

    private static IReadOnlyList<AuditField> ToAuditFields(IReadOnlyList<ReferenceRegisterFieldDefinition> fields)
        => fields
            .Select(f =>
            {
                var code = f.Code.Trim();
                var codeNorm = code.ToLowerInvariant();
                var columnCode = ReferenceRegisterNaming.NormalizeColumnCode(codeNorm);
                var name = f.Name.Trim();
                return new AuditField(codeNorm, columnCode, name, f.Ordinal, f.ColumnType, f.IsNullable);
            })
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .ToArray();

    private static bool RulesEquivalent(
        IReadOnlyList<ReferenceRegisterDimensionRule> current,
        IReadOnlyList<ReferenceRegisterDimensionRule> next)
    {
        if (current.Count != next.Count)
            return false;

        var a = current
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionId)
            .ToArray();
        
        var b = next
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionId)
            .ToArray();

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].DimensionId != b[i].DimensionId)
                return false;
            
            if (a[i].Ordinal != b[i].Ordinal)
                return false;
            
            if (a[i].IsRequired != b[i].IsRequired)
                return false;

            var aCode = a[i].DimensionCode.Trim();
            var bCode = b[i].DimensionCode.Trim();

            if (!string.Equals(aCode, bCode, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool FieldsEquivalent(
        IReadOnlyList<ReferenceRegisterField> current,
        IReadOnlyList<ReferenceRegisterFieldDefinition> next)
    {
        if (current.Count != next.Count)
            return false;

        var a = current
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .ToArray();
        
        var b = next
            .Select(x =>
            {
                var code = x.Code.Trim();
                var codeNorm = code.ToLowerInvariant();
                var columnCode = ReferenceRegisterNaming.NormalizeColumnCode(codeNorm);
                var name = x.Name.Trim();
                return new ReferenceRegisterField(
                    Guid.Empty,
                    code,
                    codeNorm,
                    columnCode,
                    name,
                    x.Ordinal,
                    x.ColumnType,
                    x.IsNullable,
                    CreatedAtUtc: DateTime.UnixEpoch,
                    UpdatedAtUtc: DateTime.UnixEpoch);
            })
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.CodeNorm, StringComparer.Ordinal)
            .ToArray();

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].CodeNorm, b[i].CodeNorm, StringComparison.Ordinal))
                return false;
            
            if (!string.Equals(a[i].ColumnCode, b[i].ColumnCode, StringComparison.Ordinal))
                return false;
            
            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal))
                return false;
            
            if (a[i].Ordinal != b[i].Ordinal)
                return false;
            
            if (a[i].ColumnType != b[i].ColumnType)
                return false;
            
            if (a[i].IsNullable != b[i].IsNullable)
                return false;
        }

        return true;
    }

    private static void ValidateDimensionRules(Guid registerId, IReadOnlyList<ReferenceRegisterDimensionRule> rules)
    {
        var seen = new HashSet<Guid>();
        var ordinals = new HashSet<int>();

        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];

            if (r.DimensionId == Guid.Empty)
                throw new ReferenceRegisterDimensionRulesValidationException(registerId, reason: "empty_dimension_id");

            if (string.IsNullOrWhiteSpace(r.DimensionCode))
                throw new ReferenceRegisterDimensionRulesValidationException(registerId, reason: "empty_dimension_code");

            var dimCodeNorm = r.DimensionCode.Trim().ToLowerInvariant();
            var expectedDimensionId = DeterministicGuid.Create($"Dimension|{dimCodeNorm}");
            
            if (r.DimensionId != expectedDimensionId)
                throw new ReferenceRegisterDimensionRulesValidationException(registerId, reason: "dimension_id_mismatch", details: new { dimensionCode = r.DimensionCode, expectedDimensionId, actualDimensionId = r.DimensionId });

            if (r.Ordinal <= 0)
                throw new ReferenceRegisterDimensionRulesValidationException(registerId, reason: "ordinal_not_positive", details: new { ordinal = r.Ordinal });

            if (!seen.Add(r.DimensionId))
                throw new ReferenceRegisterDimensionRulesValidationException(registerId, reason: "duplicate_dimension_id", details: new { dimensionId = r.DimensionId });

            if (!ordinals.Add(r.Ordinal))
                throw new ReferenceRegisterDimensionRulesValidationException(registerId, reason: "duplicate_ordinal", details: new { ordinal = r.Ordinal });
        }
    }

    private static void EnforceAppendOnlyDimensionRules(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterDimensionRule> current,
        IReadOnlyList<ReferenceRegisterDimensionRule> next)
    {
        var currentById = current.ToDictionary(x => x.DimensionId, x => x);
        var nextById = next.ToDictionary(x => x.DimensionId, x => x);

        foreach (var c in currentById.Values)
        {
            if (!nextById.TryGetValue(c.DimensionId, out var n))
                throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(registerId, reason: "remove_dimension");

            if (c.Ordinal != n.Ordinal)
                throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(registerId, reason: "change_ordinal");

            if (c.IsRequired != n.IsRequired)
                throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(registerId, reason: "change_is_required");
        }

        var addedRequired = nextById.Values
            .Where(n => !currentById.ContainsKey(n.DimensionId))
            .Where(n => n.IsRequired)
            .Select(n => n.DimensionId)
            .OrderBy(x => x)
            .ToArray();

        if (addedRequired.Length > 0)
        {
            throw new ReferenceRegisterDimensionRulesAppendOnlyViolationException(
                registerId,
                reason: "add_required_dimension",
                details: new { dimensionIds = addedRequired });
        }
    }

    private static readonly HashSet<string> ReservedFieldColumnCodes =
    [
        "record_id",
        "occurred_at_utc",
        "period_utc",
        "period_bucket_utc",
        "recorded_at_utc",
        "recorder_document_id",
        "is_deleted",
        "dimension_set_id",
    ];

    private static void ValidateFields(Guid registerId, IReadOnlyList<ReferenceRegisterFieldDefinition> fields)
    {
        var seenCodeNorm = new HashSet<string>(StringComparer.Ordinal);
        var seenOrdinal = new HashSet<int>();

        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];

            if (string.IsNullOrWhiteSpace(f.Code))
                throw new ReferenceRegisterFieldDefinitionsValidationException(registerId, reason: "empty_field_code");

            var code = f.Code.Trim();
            if (code.Length == 0)
                throw new ReferenceRegisterFieldDefinitionsValidationException(registerId, reason: "empty_field_code");

            if (string.IsNullOrWhiteSpace(f.Name))
                throw new ReferenceRegisterFieldDefinitionsValidationException(registerId, reason: "empty_field_name");

            if (f.Ordinal <= 0)
                throw new ReferenceRegisterFieldDefinitionsValidationException(registerId, reason: "ordinal_not_positive", details: new { ordinal = f.Ordinal });

            var codeNorm = code.ToLowerInvariant();
            if (!seenCodeNorm.Add(codeNorm))
                throw new ReferenceRegisterFieldDefinitionsValidationException(registerId, reason: "duplicate_field_code", details: new { codeNorm });

            if (!seenOrdinal.Add(f.Ordinal))
                throw new ReferenceRegisterFieldDefinitionsValidationException(registerId, reason: "duplicate_field_ordinal", details: new { ordinal = f.Ordinal });

            var columnCode = ReferenceRegisterNaming.NormalizeColumnCode(codeNorm);
            if (ReservedFieldColumnCodes.Contains(columnCode))
            {
                throw new ReferenceRegisterFieldDefinitionsValidationException(registerId, reason: "reserved_column_code", details: new { code, columnCode });
            }

            // We keep ColumnType.Json allowed in schema, but recommend typed columns.
        }
    }
}
