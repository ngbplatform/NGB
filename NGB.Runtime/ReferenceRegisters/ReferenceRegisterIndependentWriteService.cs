using Microsoft.Extensions.Logging;
using NGB.Accounting.PostingState;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Dimensions;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Independent-mode (no recorder) reference register write API.
///
/// Semantics:
/// - Upsert = append a new version (IsDeleted=false).
/// - Tombstone = append a new version with IsDeleted=true, copying the last known values (required for NOT NULL fields).
///
/// Both operations are idempotent via (registerId, commandId, operation).
/// </summary>
public sealed class ReferenceRegisterIndependentWriteService(
    IUnitOfWork uow,
    IReferenceRegisterRepository registers,
    IReferenceRegisterIndependentWriteStateRepository writeLog,
    IReferenceRegisterKeyLock keyLock,
    IReferenceRegisterRecordsStore recordsStore,
    IReferenceRegisterRecordsReader recordsReader,
    IReferenceRegisterDimensionRuleRepository dimensionRulesRepo,
    IDimensionSetReader dimensionSetReader,
    IDimensionSetService dimensionSets,
    IAuditLogService audit,
    ILogger<ReferenceRegisterIndependentWriteService> logger,
    TimeProvider timeProvider)
    : IReferenceRegisterIndependentWriteService
{
    public Task<ReferenceRegisterWriteResult> UpsertAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue> dimensions,
        DateTime? periodUtc,
        IReadOnlyDictionary<string, object?> values,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (dimensions is null)
            throw new NgbArgumentRequiredException(nameof(dimensions));
        
        if (values is null)
            throw new NgbArgumentRequiredException(nameof(values));
        
        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                var setId = await dimensionSets.GetOrCreateIdAsync(new DimensionBag(dimensions), innerCt);

                return await UpsertByDimensionSetIdCoreAsync(
                    registerId,
                    setId,
                    periodUtc,
                    values,
                    commandId,
                    innerCt);
            },
            ct);
    }

    public Task<ReferenceRegisterWriteResult> UpsertByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime? periodUtc,
        IReadOnlyDictionary<string, object?> values,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (values is null)
            throw new NgbArgumentRequiredException(nameof(values));
        
        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            innerCt => UpsertByDimensionSetIdCoreAsync(registerId, dimensionSetId, periodUtc, values, commandId, innerCt),
            ct);
    }

    public Task<ReferenceRegisterWriteResult> TombstoneAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue> dimensions,
        DateTime asOfUtc,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (dimensions is null)
            throw new NgbArgumentRequiredException(nameof(dimensions));
        
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                var setId = await dimensionSets.GetOrCreateIdAsync(new DimensionBag(dimensions), innerCt);

                return await TombstoneByDimensionSetIdCoreAsync(registerId, setId, asOfUtc, commandId, innerCt);
            },
            ct);
    }

    public Task<ReferenceRegisterWriteResult> TombstoneByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            innerCt => TombstoneByDimensionSetIdCoreAsync(registerId, dimensionSetId, asOfUtc, commandId, innerCt),
            ct);
    }

    private async Task<ReferenceRegisterWriteResult> UpsertByDimensionSetIdCoreAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime? periodUtc,
        IReadOnlyDictionary<string, object?> values,
        Guid commandId,
        CancellationToken ct)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        commandId.EnsureNonEmpty(nameof(commandId));
        periodUtc?.EnsureUtc(nameof(periodUtc));

        uow.EnsureActiveTransaction();

        var reg = await GetIndependentRegisterOrThrowAsync(registerId, ct);

        // IMPORTANT (deadlock prevention):
        // Acquire the per-register schema lock before *any* reads from the physical records table.
        // Otherwise, we can deadlock with concurrent EnsureSchema:
        // - Tx A (writer) reads from refreg_*__records (AccessShare), then later tries to acquire schema advisory lock.
        // - Tx B (EnsureSchema) holds schema advisory lock and tries to take an AccessExclusive DDL lock on the same table.
        //   => classic cycle (A waits for advisory lock; B waits for table lock held by A).
        // Holding the schema lock for the whole transaction prevents DDL from racing with writer reads.
        await recordsStore.EnsureSchemaAsync(registerId, ct);

        // Serialize writes for the same key.
        await keyLock.LockKeyAsync(registerId, dimensionSetId, ct);

        // Validate NEW record dimension set early (before any DDL / write pipeline).
        await ValidateDimensionSetAsync(registerId, dimensionSetId, ct);

        var startedAtUtc = timeProvider.GetUtcNowDateTime();
        var begin = await writeLog.TryBeginAsync(
            registerId,
            commandId,
            ReferenceRegisterIndependentWriteOperation.Upsert,
            startedAtUtc,
            ct);

        if (begin == PostingStateBeginResult.AlreadyCompleted)
        {
            logger.LogInformation(
                "Reference register independent upsert already completed (idempotent). registerId={RegisterId}, commandId={CommandId}",
                registerId,
                commandId);

            return ReferenceRegisterWriteResult.AlreadyCompleted;
        }

        if (begin == PostingStateBeginResult.InProgress)
            throw new ReferenceRegisterIndependentWriteAlreadyInProgressException(registerId, commandId, nameof(ReferenceRegisterIndependentWriteOperation.Upsert));

        // Ensure periodicity is compatible (store will also validate, but we need good messages).
        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            if (periodUtc is not null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "period_not_allowed_for_non_periodic", details: new { periodUtc });
        }
        else
        {
            if (periodUtc is null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "period_required_for_periodic", details: new { periodicity = reg.Periodicity });
        }

        // Audit principle (same as Operational Registers):
        // - document-based RR writes are covered by document.* audit events
        // - Independent-mode writes must emit a high-level audit event (per command), not per physical row.
        var recordedAsOfUtc = startedAtUtc;

        var effectiveAsOfUtc = reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
            ? recordedAsOfUtc
            : periodUtc!.Value;

        var old = await recordsReader.SliceLastForEffectiveMomentAsync(
            registerId,
            dimensionSetId,
            effectiveAsOfUtc,
            recordedAsOfUtc,
            recorderDocumentId: null,
            ct);
        
        await recordsStore.AppendAsync(
            registerId,
            [
                new ReferenceRegisterRecordWrite(
                    DimensionSetId: dimensionSetId,
                    PeriodUtc: periodUtc,
                    RecorderDocumentId: null,
                    Values: values,
                    IsDeleted: false)
            ],
            ct);

        var changes = new List<AuditFieldChange>(capacity: 3)
        {
            AuditLogService.Change("is_deleted", old?.IsDeleted, false),
            AuditLogService.Change("values", old?.Values, values),
        };

        if (reg.Periodicity != ReferenceRegisterPeriodicity.NonPeriodic)
            changes.Add(AuditLogService.Change("period_utc", old?.PeriodUtc, periodUtc));

        await audit.WriteAsync(
            entityKind: AuditEntityKind.ReferenceRegister,
            entityId: registerId,
            actionCode: AuditActionCodes.ReferenceRegisterRecordsUpsert,
            changes: changes,
            metadata: new
            {
                registerId,
                reg.CodeNorm,
                reg.TableCode,
                commandId,
                dimensionSetId,
                periodUtc,
                periodicity = reg.Periodicity,
                recordMode = reg.RecordMode,
            },
            ct: ct);
        
        await writeLog.MarkCompletedAsync(
            registerId,
            commandId,
            ReferenceRegisterIndependentWriteOperation.Upsert,
            timeProvider.GetUtcNowDateTime(),
            ct);

        logger.LogInformation(
            "Reference register independent upsert completed. registerId={RegisterId}, commandId={CommandId}",
            registerId,
            commandId);

        return ReferenceRegisterWriteResult.Executed;
    }

    private async Task<ReferenceRegisterWriteResult> TombstoneByDimensionSetIdCoreAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        Guid commandId,
        CancellationToken ct)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        commandId.EnsureNonEmpty(nameof(commandId));
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        uow.EnsureActiveTransaction();

        var reg = await GetIndependentRegisterOrThrowAsync(registerId, ct);

        // See UpsertByDimensionSetIdCoreAsync for the deadlock rationale.
        await recordsStore.EnsureSchemaAsync(registerId, ct);

        // Serialize writes for the same key.
        await keyLock.LockKeyAsync(registerId, dimensionSetId, ct);

        var startedAtUtc = timeProvider.GetUtcNowDateTime();
        var begin = await writeLog.TryBeginAsync(
            registerId,
            commandId,
            ReferenceRegisterIndependentWriteOperation.Tombstone,
            startedAtUtc,
            ct);

        if (begin == PostingStateBeginResult.AlreadyCompleted)
        {
            logger.LogInformation(
                "Reference register independent tombstone already completed (idempotent). registerId={RegisterId}, commandId={CommandId}",
                registerId,
                commandId);

            return ReferenceRegisterWriteResult.AlreadyCompleted;
        }

        if (begin == PostingStateBeginResult.InProgress)
            throw new ReferenceRegisterIndependentWriteAlreadyInProgressException(registerId, commandId, nameof(ReferenceRegisterIndependentWriteOperation.Tombstone));

        // Tombstone must copy values from the latest version to satisfy NOT NULL columns.
        // If no version exists (or the latest is already deleted), this is a safe no-op.
        // Persistence reader returns the latest version for the key (including tombstones).
        // We need the last values to satisfy NOT NULL columns when appending a tombstone.
        var recordedAsOfUtc = startedAtUtc;

        var last = await recordsReader.SliceLastForEffectiveMomentAsync(
            registerId,
            dimensionSetId,
            effectiveAsOfUtc: asOfUtc,
            recordedAsOfUtc: recordedAsOfUtc,
            recorderDocumentId: null,
            ct);

        if (last is null || last.IsDeleted)
        {
            await writeLog.MarkCompletedAsync(
                registerId,
                commandId,
                ReferenceRegisterIndependentWriteOperation.Tombstone,
                timeProvider.GetUtcNowDateTime(),
                ct);

            logger.LogInformation(
                "Reference register independent tombstone is a no-op (no active record). registerId={RegisterId}, commandId={CommandId}",
                registerId,
                commandId);

            return ReferenceRegisterWriteResult.Executed;
        }

        await recordsStore.AppendAsync(
            registerId,
            [
                new ReferenceRegisterRecordWrite(
                    DimensionSetId: dimensionSetId,
                    PeriodUtc: last.PeriodUtc,
                    RecorderDocumentId: null,
                    Values: last.Values,
                    IsDeleted: true)
            ],
            ct);

        await audit.WriteAsync(
            entityKind: AuditEntityKind.ReferenceRegister,
            entityId: registerId,
            actionCode: AuditActionCodes.ReferenceRegisterRecordsTombstone,
            changes:
            [
                AuditLogService.Change("is_deleted", false, true),
            ],
            metadata: new
            {
                registerId,
                commandId,
                dimensionSetId,
                asOfUtc,
                periodUtc = last.PeriodUtc,
                reg.CodeNorm,
                reg.TableCode,
                periodicity = reg.Periodicity,
                recordMode = reg.RecordMode
            },
            ct: ct);
        
        await writeLog.MarkCompletedAsync(
            registerId,
            commandId,
            ReferenceRegisterIndependentWriteOperation.Tombstone,
            timeProvider.GetUtcNowDateTime(),
            ct);

        logger.LogInformation(
            "Reference register independent tombstone completed. registerId={RegisterId}, commandId={CommandId}",
            registerId,
            commandId);

        return ReferenceRegisterWriteResult.Executed;
    }

    private async Task<ReferenceRegisterAdminItem> GetIndependentRegisterOrThrowAsync(
        Guid registerId,
        CancellationToken ct)
    {
        var reg = await registers.GetByIdAsync(registerId, ct)
                  ?? throw new ReferenceRegisterNotFoundException(registerId);

        if (reg.RecordMode != ReferenceRegisterRecordMode.Independent)
            throw new ReferenceRegisterRecordsValidationException(registerId, reason: "record_mode_not_independent", details: new { recordMode = reg.RecordMode, codeNorm = reg.CodeNorm });

        return reg;
    }

    private async Task ValidateDimensionSetAsync(Guid registerId, Guid dimensionSetId, CancellationToken ct)
    {
        // Validate just this one dimension set id.
        var rules = await dimensionRulesRepo.GetByRegisterIdAsync(registerId, ct);

        var allowed = new HashSet<Guid>(rules.Select(r => r.DimensionId));
        var required = rules.Where(r => r.IsRequired).ToArray();

        var bags = await dimensionSetReader.GetBagsByIdsAsync([dimensionSetId], ct);
        if (!bags.TryGetValue(dimensionSetId, out var bag))
            bag = DimensionBag.Empty;

        if (allowed.Count == 0)
        {
            if (!bag.IsEmpty)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "dimension_not_allowed", details: new { dimensionSetId, reason = "register_has_no_dimension_rules" });
        }
        else
        {
            var extra = bag.Items
                .Select(x => x.DimensionId)
                .Where(id => !allowed.Contains(id))
                .Distinct()
                .ToArray();

            if (extra.Length > 0)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "extra_dimensions", details: new { dimensionSetId, extraDimensionIds = extra });
        }

        if (required.Length > 0)
        {
            var present = bag.Items.Select(x => x.DimensionId).ToHashSet();

            var missing = required
                .Where(r => !present.Contains(r.DimensionId))
                .Select(r => r.DimensionCode)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            if (missing.Length > 0)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "missing_required_dimensions", details: new { dimensionSetId, missingDimensionCodes = missing });
        }
    }
}
