using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.ReferenceRegisters;

public sealed class ReferenceRegisterRecordsApplier(
    IReferenceRegisterWriteEngine writeEngine,
    IReferenceRegisterRecordsStore recordsStore,
    IReferenceRegisterDimensionRuleRepository dimensionRulesRepo,
    IDimensionSetReader dimensionSetReader)
    : IReferenceRegisterRecordsApplier
{
    public async Task<ReferenceRegisterWriteResult> ApplyRecordsForDocumentAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        IReadOnlyList<ReferenceRegisterRecordWrite> recordsToApply,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (recordsToApply is null)
            throw new NgbArgumentRequiredException(nameof(recordsToApply));
        
        registerId.EnsureNonEmpty(nameof(registerId));
        documentId.EnsureNonEmpty(nameof(documentId));
        
        // Validate NEW record dimension sets early (before any DDL / write pipeline).
        // IMPORTANT:
        // - We validate ONLY the payload (new records) for Post/Repost.
        // - We do NOT validate Unpost writes, because rules may change over time and must not prevent rollback.
        if (operation is ReferenceRegisterWriteOperation.Post or ReferenceRegisterWriteOperation.Repost)
            await ValidateNewRecordDimensionSetsAsync(registerId, recordsToApply, ct);

        // Defensive guard: records must not claim a different recorder doc id.
        for (var i = 0; i < recordsToApply.Count; i++)
        {
            var r = recordsToApply[i];

            if (r.PeriodUtc is not null)
                r.PeriodUtc.Value.EnsureUtc($"recordsToApply[{i}].PeriodUtc");

            if (r.RecorderDocumentId is not null && r.RecorderDocumentId.Value != documentId)
            {
                throw new ReferenceRegisterRecordsValidationException(
                    registerId,
                    reason: "recorder_document_id_mismatch",
                    details: new { index = i, expectedDocumentId = documentId, actualRecorderDocumentId = r.RecorderDocumentId });
            }
        }

        return await writeEngine.ExecuteAsync(
            registerId,
            documentId,
            operation,
            innerCt => recordsStore.AppendAsync(registerId, recordsToApply, innerCt),
            manageTransaction,
            ct);
    }

    private async Task ValidateNewRecordDimensionSetsAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterRecordWrite> recordsToApply,
        CancellationToken ct)
    {
        if (recordsToApply.Count == 0)
            return;

        var rules = await dimensionRulesRepo.GetByRegisterIdAsync(registerId, ct);

        var allowed = new HashSet<Guid>(rules.Select(r => r.DimensionId));
        var required = rules.Where(r => r.IsRequired).ToArray();

        var setIds = recordsToApply.Select(r => r.DimensionSetId).Distinct().ToArray();
        var bags = await dimensionSetReader.GetBagsByIdsAsync(setIds, ct);

        foreach (var setId in setIds)
        {
            if (!bags.TryGetValue(setId, out var bag))
                bag = DimensionBag.Empty;

            // Extra dimensions are not allowed.
            if (allowed.Count == 0)
            {
                if (!bag.IsEmpty)
                {
                    throw new ReferenceRegisterRecordsValidationException(
                        registerId,
                        reason: "dimension_not_allowed",
                        details: new { dimensionSetId = setId, reason = "register_has_no_dimension_rules" });
                }
            }
            else
            {
                var extra = bag.Items
                    .Select(x => x.DimensionId)
                    .Where(id => !allowed.Contains(id))
                    .Distinct()
                    .ToArray();

                if (extra.Length > 0)
                {
                    throw new ReferenceRegisterRecordsValidationException(
                        registerId,
                        reason: "extra_dimensions",
                        details: new { dimensionSetId = setId, extraDimensionIds = extra });
                }
            }

            // Missing required dimensions are not allowed.
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
                {
                    throw new ReferenceRegisterRecordsValidationException(
                        registerId,
                        reason: "missing_required_dimensions",
                        details: new { dimensionSetId = setId, missingDimensionCodes = missing });
                }
            }
        }
    }
}
