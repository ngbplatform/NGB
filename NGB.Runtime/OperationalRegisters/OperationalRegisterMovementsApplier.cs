using NGB.Core.Dimensions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.OperationalRegisters;

public sealed class OperationalRegisterMovementsApplier(
    IOperationalRegisterWriteEngine writeEngine,
    IOperationalRegisterMovementsStore movements,
    IOperationalRegisterMovementsReader movementsReader,
    IOperationalRegisterDimensionRuleRepository dimensionRulesRepo,
    IDimensionSetReader dimensionSetReader)
    : IOperationalRegisterMovementsApplier
{
    public async Task<OperationalRegisterWriteResult> ApplyMovementsForDocumentAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        IReadOnlyList<OperationalRegisterMovement> movementsToApply,
        IReadOnlyCollection<DateOnly>? affectedPeriods = null,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (movementsToApply is null)
            throw new NgbArgumentRequiredException(nameof(movementsToApply));
        
        registerId.EnsureNonEmpty(nameof(registerId));
        documentId.EnsureNonEmpty(nameof(documentId));
        // Defensive guard: all provided movements must belong to the same document.
        for (var i = 0; i < movementsToApply.Count; i++)
        {
            movementsToApply[i].OccurredAtUtc.EnsureUtc($"movementsToApply[{i}].OccurredAtUtc");

            if (movementsToApply[i].DocumentId != documentId)
            {
                throw new NgbArgumentInvalidException(
                    $"movementsToApply[{i}].DocumentId",
                    $"All movements must have the same DocumentId. Expected '{documentId}', got '{movementsToApply[i].DocumentId}'.");
            }
        }

        // Validate NEW movement dimension sets early (before any DDL / write pipeline).
        // IMPORTANT:
        // - We validate ONLY the payload (new movements) for Post/Repost.
        // - We do NOT validate Unpost/storno reads, because rules may change over time and must not prevent rollback.
        if (operation is OperationalRegisterWriteOperation.Post or OperationalRegisterWriteOperation.Repost)
            await ValidateNewMovementDimensionSetsAsync(registerId, movementsToApply, ct);

        // IMPORTANT:
        // Ensure physical schema OUTSIDE the business write transaction (when we manage the transaction).
        // This avoids deadlocks between dynamic DDL (ALTER/CREATE INDEX) and business advisory locks
        // (document/month) held by the write pipeline.
        //
        // When manageTransaction == false, we MUST NOT touch the DB before the UoW verifies an ambient
        // transaction exists. Otherwise, callers expecting a deterministic "missing ambient transaction"
        // invariant violation might observe unrelated DB exceptions (e.g., register not found).
        //
        // EnsureSchemaAsync itself is serialized via OperationalRegisterSchema advisory lock.
        if (manageTransaction)
            await movements.EnsureSchemaAsync(registerId, ct);

        IReadOnlyCollection<DateOnly> months;

        if (affectedPeriods is not null)
        {
            months = affectedPeriods;
        }
        else
        {
            // Derive months from payload + existing movements (needed for Unpost/Repost).
            var set = new HashSet<DateOnly>();

            // New movements (Post/Repost)
            foreach (var m in DeriveAffectedMonths(movementsToApply))
            {
                set.Add(m);
            }

            // Existing movements (Unpost/Repost)
            if (operation is OperationalRegisterWriteOperation.Unpost or OperationalRegisterWriteOperation.Repost)
            {
                var existing = await movementsReader.GetDistinctMonthsByDocumentAsync(registerId, documentId, ct);
                foreach (var m in existing)
                {
                    set.Add(m);
                }
            }

            months = set;
        }

        return await writeEngine.ExecuteAsync(
            registerId,
            documentId,
            operation,
            months,
            async innerCt =>
            {
                // External transaction mode: ensure physical schema inside the ambient transaction
                // (after UoW has validated transaction presence).
                if (!manageTransaction)
                    await movements.EnsureSchemaAsync(registerId, innerCt);

                switch (operation)
                {
                    case OperationalRegisterWriteOperation.Post:
                        await movements.AppendAsync(registerId, movementsToApply, innerCt);
                        break;

                    case OperationalRegisterWriteOperation.Unpost:
                        // Unpost is a storno append (no deletes).
                        await movements.AppendStornoByDocumentAsync(registerId, documentId, innerCt);
                        break;

                    case OperationalRegisterWriteOperation.Repost:
                        // Repost cancels previous state (storno), then applies the new movements.
                        await movements.AppendStornoByDocumentAsync(registerId, documentId, innerCt);
                        await movements.AppendAsync(registerId, movementsToApply, innerCt);
                        break;

                    default:
                        throw new NgbArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation");
                }
            },
            manageTransaction,
            ct);
    }

    private async Task ValidateNewMovementDimensionSetsAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterMovement> movementsToApply,
        CancellationToken ct)
    {
        if (movementsToApply.Count == 0)
            return;

        var rules = await dimensionRulesRepo.GetByRegisterIdAsync(registerId, ct);

        var allowed = new HashSet<Guid>(rules.Select(r => r.DimensionId));
        var required = rules.Where(r => r.IsRequired).ToArray();

        var setIds = movementsToApply.Select(m => m.DimensionSetId).Distinct().ToArray();
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
                    throw new NgbArgumentInvalidException(
                        nameof(OperationalRegisterMovement.DimensionSetId),
                        $"DimensionSetId '{setId}' contains a dimension not allowed for this register (register has no dimension rules)."
                    );
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
                    throw new NgbArgumentInvalidException(
                        nameof(OperationalRegisterMovement.DimensionSetId),
                        $"DimensionSetId '{setId}' contains a dimension not allowed for this register. " +
                        $"Extra dimension id(s): {string.Join(", ", extra)}."
                    );
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
                    throw new NgbArgumentInvalidException(
                        nameof(OperationalRegisterMovement.DimensionSetId),
                        $"DimensionSetId '{setId}' is missing required dimensions for this register: {string.Join(", ", missing)}."
                    );
                }
            }
        }
    }

    private static IReadOnlyCollection<DateOnly> DeriveAffectedMonths(IReadOnlyList<OperationalRegisterMovement> movements)
    {
        if (movements.Count == 0)
            return [];

        // Normalize to month-start (UTC). WriteEngine will normalize again, but this keeps the API intuitive.
        var months = new HashSet<DateOnly>();
        
        for (var i = 0; i < movements.Count; i++)
        {
            months.Add(OperationalRegisterPeriod.MonthStart(movements[i].OccurredAtUtc));
        }

        return months;
    }
}
